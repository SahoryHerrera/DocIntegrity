using DocIntegrity.Api.DTOs;
using DocIntegrity.Api.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace DocIntegrity.Api.Services.Comparison;

public sealed record ComparisonInput(string FileName, byte[] Content);

public interface IDocumentComparisonService
{
    // lease: turno de ComparisonGate; si la extracción excede el tiempo, el turno
    // se retiene hasta que termine. No escribe en disco, base de datos ni blockchain.
    Task<ComparisonResultDto> CompareAsync(
        ComparisonInput original,
        ComparisonInput modified,
        ComparisonGate.Lease? lease,
        CancellationToken cancellationToken);
}

// Comparación determinista de dos PDF:
//   1. SHA-256 iguales → identical (no se extrae texto).
//   2. Extracción por páginas (PdfPig) con informe de calidad.
//   3. Normalización documentada y diff de Myers por palabras (respaldo por líneas).
//   4. Veredicto: same_text solo si el texto coincide y ambas extracciones son completas.
public class DocumentComparisonService : IDocumentComparisonService
{
    public static class Verdicts
    {
        public const string Identical = "identical";
        public const string SameText = "same_text";
        public const string Modified = "modified";
        public const string NotComparable = "not_comparable";
    }

    private readonly IPdfPageExtractor _extractor;
    private readonly IHashService _hashService;
    private readonly ComparisonOptions _options;
    private readonly ILogger<DocumentComparisonService> _logger;

    public DocumentComparisonService(
        IPdfPageExtractor extractor,
        IHashService hashService,
        IOptions<ComparisonOptions> options,
        ILogger<DocumentComparisonService> logger)
    {
        _extractor = extractor;
        _hashService = hashService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ComparisonResultDto> CompareAsync(
        ComparisonInput original,
        ComparisonInput modified,
        ComparisonGate.Lease? lease,
        CancellationToken cancellationToken)
    {
        var result = new ComparisonResultDto
        {
            ComparedAt = DateTime.UtcNow,
            Normalization = TextNormalizer.Steps
                .Select(step => new NormalizationStepDto { Code = step.Code, Description = step.Description })
                .ToList(),
            Files = new ComparisonFilesDto
            {
                Original = await DescribeFileAsync(original),
                Modified = await DescribeFileAsync(modified)
            }
        };

        // 1. Huellas.
        if (result.Files.Original.Sha256 == result.Files.Modified.Sha256)
        {
            result.Verdict = Verdicts.Identical;
            result.Reliability = "not_needed";
            result.Warnings.Add(Info("identical_files", "Los dos archivos tienen el mismo SHA-256: son el mismo archivo, byte a byte."));
            return result;
        }

        // 2. Extracción, con tiempo máximo.
        var extraction = await ExtractBothAsync(original, modified, lease, cancellationToken);

        if (extraction is null)
        {
            return NotComparable(result, "timeout", "none",
                Blocking("timeout", $"La extracción de texto superó el tiempo máximo de {_options.ExtractionTimeoutSeconds:0} s.", null));
        }

        var (docA, docB) = extraction.Value;
        var wordsA = TextNormalizer.ToWords(docA.Lines);
        var wordsB = TextNormalizer.ToWords(docB.Lines);

        Describe(result.Files.Original, docA, wordsA.Count);
        Describe(result.Files.Modified, docB, wordsB.Count);
        result.MetadataDifferences = CompareMetadata(docA, docB);
        AddQualityWarnings(result.Warnings, docA, "original");
        AddQualityWarnings(result.Warnings, docB, "modified");

        var blocked = (ExtractionStatus.IsBlocking(docA.Status), ExtractionStatus.IsBlocking(docB.Status)) switch
        {
            (true, _) => (Doc: docA, File: "original"),
            (_, true) => (Doc: docB, File: "modified"),
            _ => default
        };

        if (blocked.Doc is not null)
        {
            if (ExtractionStatus.IsBlocking(docA.Status) && ExtractionStatus.IsBlocking(docB.Status) && blocked.File == "original")
            {
                // Los dos documentos tienen un problema: se informan ambos.
                result.Warnings.Add(BlockingFor(docB, "modified"));
            }

            var reason = blocked.Doc.Status == ExtractionStatus.None ? "no_text" : blocked.Doc.Status;
            return NotComparable(result, reason, "none", BlockingFor(blocked.Doc, blocked.File));
        }

        var bothComplete = docA.Status == ExtractionStatus.Complete && docB.Status == ExtractionStatus.Complete;

        // 3. Diff por palabras; si es demasiado costoso, por líneas.
        var granularity = "word";
        var unitsA = wordsA;
        var unitsB = wordsB;
        var segments = Diff(unitsA, unitsB);

        if (segments is null)
        {
            granularity = "line";
            unitsA = TextNormalizer.ToLines(docA.Lines);
            unitsB = TextNormalizer.ToLines(docB.Lines);
            segments = Diff(unitsA, unitsB);
        }

        if (segments is null)
        {
            // El diff solo agota el presupuesto si los textos difieren: hay cambios, pero sin detalle.
            result.Verdict = Verdicts.Modified;
            result.Reliability = bothComplete ? "complete" : "partial";
            result.Granularity = "none";
            result.DiffIncomplete = true;
            result.Warnings.Add(Warning("too_many_differences",
                "Los textos son distintos, pero tienen demasiadas diferencias para detallarlas dentro de los límites de tiempo y memoria.", null));
            return result;
        }

        result.Granularity = granularity;

        if (segments.All(segment => segment.Kind == DiffKind.Equal))
        {
            if (bothComplete)
            {
                result.Verdict = Verdicts.SameText;
                result.Reliability = "complete";
                result.Stats = new ComparisonStatsDto { UnitsUnchanged = unitsA.Count, Similarity = 1 };
                result.Warnings.Add(Info("same_text_scope",
                    "El texto extraído coincide, pero los archivos son distintos. No se comparan imágenes, firmas, formato ni maquetación; revisa también las diferencias de metadatos."));
                return result;
            }

            // Sin diferencias en lo que se pudo leer, pero hay páginas que no se pudieron leer.
            return NotComparable(result, "incomplete_extraction", "partial",
                Blocking("incomplete_extraction",
                    "No se encontraron diferencias en el texto legible, pero la extracción de algún documento es incompleta: no se puede afirmar que el texto sea el mismo.", null));
        }

        // 4. Cambios.
        var unreliableA = UnreliablePages(docA);
        var unreliableB = UnreliablePages(docB);

        var (changes, stats) = ChangeBuilder.Build(unitsA, unitsB, segments, new ChangeBuildContext
        {
            HeadingsOriginal = SectionDetector.Detect(docA.Lines),
            HeadingsModified = SectionDetector.Detect(docB.Lines),
            UnreliablePagesOriginal = unreliableA,
            UnreliablePagesModified = unreliableB,
            Separator = granularity == "word" ? " " : "\n",
            MergeShortGaps = granularity == "word",
            ContextUnits = granularity == "word" ? _options.ContextWords : 1,
            MaxTextLength = _options.MaxChangeTextLength
        });

        result.Verdict = Verdicts.Modified;
        result.Reliability = bothComplete ? "complete" : "partial";
        result.Stats = stats;
        result.TotalChanges = changes.Count;
        result.ChangesTruncated = changes.Count > _options.MaxChangesInResponse;
        result.Changes = changes.Take(_options.MaxChangesInResponse).ToList();

        if (!bothComplete)
        {
            result.Warnings.Add(Warning("changes_may_be_artifacts",
                "La extracción de algún documento es incompleta. Los cambios marcados con onIncompletePage pueden deberse a la extracción y no al documento.", null));
        }

        if (granularity == "line")
        {
            result.Warnings.Add(Warning("coarse_granularity",
                "Los documentos son muy distintos: los cambios se muestran por líneas completas en lugar de por palabras.", null));
        }

        if (result.ChangesTruncated)
        {
            result.Warnings.Add(Warning("changes_truncated",
                $"Se detectaron {changes.Count} cambios; la respuesta incluye los primeros {_options.MaxChangesInResponse}.", null));
        }

        _logger.LogInformation("Comparación: {Verdict}, {Changes} cambios, granularidad {Granularity}",
            result.Verdict, changes.Count, granularity);

        return result;
    }

    private async Task<(ExtractedDocument, ExtractedDocument)?> ExtractBothAsync(
        ComparisonInput original,
        ComparisonInput modified,
        ComparisonGate.Lease? lease,
        CancellationToken cancellationToken)
    {
        var limit = TimeSpan.FromSeconds(_options.ExtractionTimeoutSeconds);
        var timeout = new CancellationTokenSource(limit);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var work = Task.Run(
            () => (_extractor.Extract(original.Content, linked.Token), _extractor.Extract(modified.Content, linked.Token)),
            CancellationToken.None);

        // Los tokens se liberan cuando la extracción termina de verdad.
        _ = work.ContinueWith(_ =>
        {
            linked.Dispose();
            timeout.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

        try
        {
            // El extractor revisa el token entre páginas; el margen cubre una página que no termina.
            var (docA, docB) = await work.WaitAsync(limit + TimeSpan.FromSeconds(_options.ExtractionGraceSeconds), cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            return (docA, docB);
        }
        catch (TimeoutException)
        {
            lease?.HoldUntil(work);
            return null;
        }
        catch (OperationCanceledException)
        {
            lease?.HoldUntil(work);
            throw;
        }
    }

    private IReadOnlyList<DiffSegment>? Diff(List<TextUnit> a, List<TextUnit> b)
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        int Id(TextUnit unit) => ids.TryGetValue(unit.Text, out var id) ? id : ids[unit.Text] = ids.Count;

        var aIds = a.Select(Id).ToArray();
        var bIds = b.Select(Id).ToArray();

        return MyersDiff.Compute(aIds, bIds, _options.MaxDiffOperations);
    }

    private async Task<ComparisonFileDto> DescribeFileAsync(ComparisonInput input) => new()
    {
        FileName = Path.GetFileName(input.FileName),
        Size = input.Content.LongLength,
        Sha256 = await _hashService.CalculateSha256Async(new MemoryStream(input.Content, writable: false))
    };

    private static void Describe(ComparisonFileDto file, ExtractedDocument document, int words)
    {
        file.PageCount = document.PageCount;
        file.PdfVersion = document.PdfVersion;
        file.Extraction = new ExtractionReportDto
        {
            Status = document.Status,
            Encrypted = document.Encrypted,
            Words = ExtractionStatus.IsBlocking(document.Status) ? document.ApproximateWords : words,
            PagesWithText = document.Pages.Count(p => p.Status is PageStatus.Text or PageStatus.Garbled),
            BlankPages = PagesWith(document, PageStatus.Blank),
            SuspectedScannedPages = PagesWith(document, PageStatus.SuspectedScanned),
            GarbledPages = PagesWith(document, PageStatus.Garbled),
            FailedPages = PagesWith(document, PageStatus.Failed)
        };
    }

    private static List<int> PagesWith(ExtractedDocument document, string status) =>
        document.Pages.Where(p => p.Status == status).Select(p => p.Number).ToList();

    private static HashSet<int> UnreliablePages(ExtractedDocument document) =>
        document.Pages.Where(p => !PageStatus.IsReliable(p.Status)).Select(p => p.Number).ToHashSet();

    private static List<MetadataDifferenceDto> CompareMetadata(ExtractedDocument a, ExtractedDocument b)
    {
        var differences = new List<MetadataDifferenceDto>();

        // Solo si ambos documentos se pudieron abrir.
        if (a.PageCount is null || b.PageCount is null)
        {
            return differences;
        }

        if (a.PageCount != b.PageCount)
        {
            differences.Add(new MetadataDifferenceDto { Field = "pageCount", Original = a.PageCount.ToString(), Modified = b.PageCount.ToString() });
        }

        if (a.PdfVersion != b.PdfVersion)
        {
            differences.Add(new MetadataDifferenceDto { Field = "pdfVersion", Original = a.PdfVersion, Modified = b.PdfVersion });
        }

        foreach (var key in a.Metadata.Keys.Union(b.Metadata.Keys).Order(StringComparer.Ordinal))
        {
            a.Metadata.TryGetValue(key, out var valueA);
            b.Metadata.TryGetValue(key, out var valueB);

            if (valueA != valueB)
            {
                differences.Add(new MetadataDifferenceDto { Field = key, Original = valueA, Modified = valueB });
            }
        }

        return differences;
    }

    private static void AddQualityWarnings(List<ComparisonWarningDto> warnings, ExtractedDocument document, string file)
    {
        if (ExtractionStatus.IsBlocking(document.Status))
        {
            return;
        }

        void Add(string code, string severity, string message, List<int> pages)
        {
            if (pages.Count > 0)
            {
                warnings.Add(new ComparisonWarningDto { Code = code, Severity = severity, Message = message, File = file, Pages = pages });
            }
        }

        Add("suspected_scanned_pages", "warning",
            "Estas páginas parecen escaneadas (imagen sin texto): su contenido no se puede comparar.",
            PagesWith(document, PageStatus.SuspectedScanned));
        Add("garbled_text_pages", "warning",
            "El texto de estas páginas no se extrajo de forma legible (fuentes sin correspondencia Unicode).",
            PagesWith(document, PageStatus.Garbled));
        Add("failed_pages", "warning",
            "Estas páginas no se pudieron leer.",
            PagesWith(document, PageStatus.Failed));
        Add("blank_pages", "info",
            "Estas páginas no tienen texto ni imágenes.",
            PagesWith(document, PageStatus.Blank));

        if (document.Encrypted)
        {
            warnings.Add(new ComparisonWarningDto
            {
                Code = "encrypted_permissions_only", Severity = "info", File = file,
                Message = "El PDF está cifrado sin contraseña de apertura (solo restricciones de permisos); su texto se pudo leer."
            });
        }
    }

    private ComparisonWarningDto BlockingFor(ExtractedDocument document, string file) => document.Status switch
    {
        ExtractionStatus.Encrypted => Blocking("encrypted", "El PDF está protegido con contraseña: no se puede leer su texto.", file),
        ExtractionStatus.Unreadable => Blocking("unreadable", "El archivo tiene la firma de un PDF, pero su estructura está dañada o no se puede interpretar.", file),
        ExtractionStatus.TooManyPages => Blocking("too_many_pages", $"El PDF tiene {document.PageCount} páginas; el máximo es {_options.MaxPages}.", file),
        ExtractionStatus.TooMuchText => Blocking("too_much_text", $"El documento supera el máximo de {_options.MaxWordsPerDocument} palabras.", file),
        ExtractionStatus.Timeout => Blocking("timeout", $"La extracción de texto superó el tiempo máximo de {_options.ExtractionTimeoutSeconds:0} s.", file),
        _ => Blocking("no_text", "El PDF no tiene capa de texto (probablemente está escaneado). Compararlo requeriría OCR, que no está disponible.", file)
    };

    private static ComparisonResultDto NotComparable(ComparisonResultDto result, string reason, string reliability, ComparisonWarningDto warning)
    {
        result.Verdict = Verdicts.NotComparable;
        result.NotComparableReason = reason;
        result.Reliability = reliability;
        result.Granularity = "none";
        result.Warnings.Insert(0, warning);
        return result;
    }

    private static ComparisonWarningDto Blocking(string code, string message, string? file) =>
        new() { Code = code, Severity = "blocking", Message = message, File = file };

    private static ComparisonWarningDto Warning(string code, string message, string? file) =>
        new() { Code = code, Severity = "warning", Message = message, File = file };

    private static ComparisonWarningDto Info(string code, string message) =>
        new() { Code = code, Severity = "info", Message = message };
}
