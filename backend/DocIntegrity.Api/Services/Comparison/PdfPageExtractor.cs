using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace DocIntegrity.Api.Services.Comparison;

// Estado de la extracción de un documento completo.
public static class ExtractionStatus
{
    public const string Complete = "complete";          // todas las páginas con texto legible (o en blanco)
    public const string Partial = "partial";            // alguna página escaneada, ilegible o que falló
    public const string None = "none";                  // ninguna página con texto: no hay capa de texto
    public const string Encrypted = "encrypted";        // requiere contraseña
    public const string Unreadable = "unreadable";      // firma %PDF- correcta, pero la estructura no se puede leer
    public const string TooManyPages = "too_many_pages";
    public const string TooMuchText = "too_much_text";
    public const string Timeout = "timeout";

    // Estados con los que no hay texto suficiente para comparar.
    public static bool IsBlocking(string status) =>
        status is None or Encrypted or Unreadable or TooManyPages or TooMuchText or Timeout;
}

// Estado de cada página.
public static class PageStatus
{
    public const string Text = "text";
    public const string Blank = "blank";                       // sin texto ni imágenes
    public const string SuspectedScanned = "suspected_scanned"; // imágenes y (casi) sin texto
    public const string Garbled = "garbled";                   // texto con muchos caracteres no legibles
    public const string Failed = "failed";                     // PdfPig no pudo leer la página

    public static bool IsReliable(string status) => status is Text or Blank;
}

public sealed record ExtractedLine(int Page, int Line, string Text);

public sealed record PageReport(int Number, string Status, int NonWhitespaceChars, int Images, double ImageCoverage);

public sealed class ExtractedDocument
{
    public string Status { get; init; } = ExtractionStatus.Unreadable;
    public bool Encrypted { get; init; }
    public int? PageCount { get; init; }
    public string? PdfVersion { get; init; }
    public IReadOnlyList<ExtractedLine> Lines { get; init; } = [];
    public IReadOnlyList<PageReport> Pages { get; init; } = [];
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    public int ApproximateWords { get; init; }
}

public interface IPdfPageExtractor
{
    // Nunca lanza por el contenido del PDF: los problemas se informan en Status.
    // Solo propaga OperationCanceledException si se cancela la solicitud.
    ExtractedDocument Extract(byte[] pdf, CancellationToken cancellationToken);
}

public class PdfPageExtractor : IPdfPageExtractor
{
    // Una página con imágenes que cubren al menos la mitad de su área y menos
    // de 50 caracteres se trata como escaneada.
    private const double ScannedCoverage = 0.5;
    private const int ScannedMaxChars = 50;

    // Proporción de caracteres no legibles (U+FFFD, control, uso privado) para
    // considerar el texto de una página ilegible: fuentes sin mapa a Unicode.
    private const double GarbledRatio = 0.10;

    private readonly ComparisonOptions _options;
    private readonly ILogger<PdfPageExtractor> _logger;

    public PdfPageExtractor(IOptions<ComparisonOptions> options, ILogger<PdfPageExtractor> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public ExtractedDocument Extract(byte[] pdf, CancellationToken cancellationToken)
    {
        PdfDocument document;

        try
        {
            document = PdfDocument.Open(pdf, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });
        }
        catch (PdfDocumentEncryptedException)
        {
            return new ExtractedDocument { Status = ExtractionStatus.Encrypted, Encrypted = true };
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            // Solo el tipo: el mensaje de PdfPig puede incluir contenido del archivo.
            _logger.LogInformation("PDF ilegible al abrirlo ({Exception})", ex.GetType().Name);
            return new ExtractedDocument { Status = ExtractionStatus.Unreadable };
        }

        using (document)
        {
            int pageCount;
            bool encrypted;
            string? version;

            try
            {
                pageCount = document.NumberOfPages;
                encrypted = document.IsEncrypted;
                version = document.Version.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                _logger.LogInformation("PDF ilegible al leer su estructura ({Exception})", ex.GetType().Name);
                return new ExtractedDocument { Status = ExtractionStatus.Unreadable };
            }

            var metadata = ReadMetadata(document);

            if (pageCount > _options.MaxPages)
            {
                return new ExtractedDocument
                {
                    Status = ExtractionStatus.TooManyPages, PageCount = pageCount, Encrypted = encrypted,
                    PdfVersion = version, Metadata = metadata
                };
            }

            if (pageCount == 0)
            {
                return new ExtractedDocument { Status = ExtractionStatus.Unreadable, PageCount = 0, PdfVersion = version, Metadata = metadata };
            }

            var lines = new List<ExtractedLine>();
            var pages = new List<PageReport>(pageCount);
            var words = 0;

            for (var number = 1; number <= pageCount; number++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return new ExtractedDocument { Status = ExtractionStatus.Timeout, PageCount = pageCount, Encrypted = encrypted, PdfVersion = version, Metadata = metadata };
                }

                PageReport report;
                List<ExtractedLine> pageLines;

                try
                {
                    (report, pageLines) = ExtractPage(document, number);
                }
                catch (PdfDocumentEncryptedException)
                {
                    return new ExtractedDocument { Status = ExtractionStatus.Encrypted, Encrypted = true, PageCount = pageCount, PdfVersion = version, Metadata = metadata };
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
                {
                    _logger.LogInformation("No se pudo leer la página {Page} ({Exception})", number, ex.GetType().Name);
                    report = new PageReport(number, PageStatus.Failed, 0, 0, 0);
                    pageLines = [];
                }

                pages.Add(report);
                lines.AddRange(pageLines);

                foreach (var line in pageLines)
                {
                    words += CountWords(line.Text);
                }

                if (words > _options.MaxWordsPerDocument)
                {
                    return new ExtractedDocument { Status = ExtractionStatus.TooMuchText, PageCount = pageCount, Encrypted = encrypted, PdfVersion = version, Metadata = metadata, ApproximateWords = words };
                }
            }

            return new ExtractedDocument
            {
                Status = Classify(pages),
                Encrypted = encrypted,
                PageCount = pageCount,
                PdfVersion = version,
                Lines = lines,
                Pages = pages,
                Metadata = metadata,
                ApproximateWords = words
            };
        }
    }

    private static (PageReport, List<ExtractedLine>) ExtractPage(PdfDocument document, int number)
    {
        var page = document.GetPage(number);

        // Mismo extractor que usa el análisis con IA (PdfTextExtractorService).
        var text = ContentOrderTextExtractor.GetText(page) ?? string.Empty;

        var images = 0;
        double imageArea = 0;

        try
        {
            foreach (var image in page.GetImages())
            {
                images++;
                imageArea += Math.Abs(image.BoundingBox.Width * image.BoundingBox.Height);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            // Si las imágenes no se pueden enumerar, se valora solo el texto.
        }

        var pageArea = Math.Abs(page.Width * page.Height);
        var coverage = pageArea > 0 ? Math.Min(1, imageArea / pageArea) : 0;

        var (status, nonWhitespace) = ClassifyPage(text, images, coverage);

        var lines = new List<ExtractedLine>();
        var lineNumber = 0;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            lines.Add(new ExtractedLine(number, ++lineNumber, line));
        }

        return (new PageReport(number, status, nonWhitespace, images, Math.Round(coverage, 3)), lines);
    }

    // Estado de una página según su texto extraído y sus imágenes.
    public static (string Status, int NonWhitespaceChars) ClassifyPage(string text, int images, double coverage)
    {
        var nonWhitespace = 0;
        var unreadable = 0;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            nonWhitespace++;

            if (c == '\uFFFD' || char.IsControl(c) || (c >= '\uE000' && c <= '\uF8FF'))
            {
                unreadable++;
            }
        }

        var status =
            nonWhitespace == 0 ? (images > 0 ? PageStatus.SuspectedScanned : PageStatus.Blank)
            : (double)unreadable / nonWhitespace > GarbledRatio ? PageStatus.Garbled
            : coverage >= ScannedCoverage && nonWhitespace < ScannedMaxChars ? PageStatus.SuspectedScanned
            : PageStatus.Text;

        return (status, nonWhitespace);
    }

    private static string Classify(IReadOnlyList<PageReport> pages)
    {
        if (!pages.Any(p => p.Status is PageStatus.Text or PageStatus.Garbled))
        {
            return ExtractionStatus.None;
        }

        return pages.All(p => PageStatus.IsReliable(p.Status))
            ? ExtractionStatus.Complete
            : ExtractionStatus.Partial;
    }

    private static int CountWords(string text)
    {
        var count = 0;
        var inWord = false;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                count++;
            }
        }

        return count;
    }

    private static Dictionary<string, string> ReadMetadata(PdfDocument document)
    {
        var metadata = new Dictionary<string, string>();

        try
        {
            var info = document.Information;

            void Add(string key, string? value)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    metadata[key] = value;
                }
            }

            Add("title", info.Title);
            Add("author", info.Author);
            Add("subject", info.Subject);
            Add("keywords", info.Keywords);
            Add("creator", info.Creator);
            Add("producer", info.Producer);
            Add("creationDate", info.CreationDate);
            Add("modifiedDate", info.ModifiedDate);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            // Metadatos ilegibles: se comparan solo los que se pudieron leer.
        }

        return metadata;
    }
}
