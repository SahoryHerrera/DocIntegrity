using System.Text.Json;
using DocIntegrity.Api.DTOs;
using DocIntegrity.Api.Services;
using DocIntegrity.Api.Services.Comparison;
using DocIntegrity.Api.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocIntegrity.Api.Tests;

// Comparaciones completas (PdfPig + normalización + Myers + agrupación) con PDF ficticios.
public class ComparisonServiceTests
{
    private static string[] Contract(string pago = "mensual", string importe = "1.000", string fecha = "12/10/2026", string correo = "pagos@empresa.com") =>
    [
        "CONTRATO DE SERVICIOS",
        "1. Objeto",
        $"El cliente pagara el servicio {pago}.",
        "2. Precio",
        $"El importe total es de {importe} euros.",
        $"Fecha de firma: {fecha}",
        $"Contacto: {correo}"
    ];

    private static readonly string[] SecondPage =
    [
        "3. Vigencia",
        "El contrato tiene una vigencia de un ano.",
        "Puede renovarse por acuerdo de las partes.",
        "4. Firmas"
    ];

    internal static DocumentComparisonService Service(ComparisonOptions? options = null, IPdfPageExtractor? extractor = null)
    {
        var resolved = Options.Create(options ?? new ComparisonOptions());

        return new DocumentComparisonService(
            extractor ?? new PdfPageExtractor(resolved, NullLogger<PdfPageExtractor>.Instance),
            new HashService(),
            resolved,
            NullLogger<DocumentComparisonService>.Instance);
    }

    private static Task<ComparisonResultDto> Compare(byte[] original, byte[] modified, ComparisonOptions? options = null) =>
        Service(options).CompareAsync(new("original.pdf", original), new("nueva.pdf", modified), null, CancellationToken.None);

    // ---------------------------------------------------------------- veredictos

    [Fact]
    public async Task IdenticalFiles_AreIdentical_WithoutExtraction()
    {
        var pdf = PdfFixtures.Lines(Contract());

        var result = await Compare(pdf, (byte[])pdf.Clone());

        Assert.Equal("identical", result.Verdict);
        Assert.Equal("not_needed", result.Reliability);
        Assert.Equal(result.Files.Original.Sha256, result.Files.Modified.Sha256);
        Assert.Null(result.Files.Original.Extraction);
        Assert.Empty(result.Changes);
    }

    [Fact]
    public async Task OneWordChanged_IsReplaced_WithLocationAndSection()
    {
        var result = await Compare(PdfFixtures.Lines(Contract()), PdfFixtures.Lines(Contract(pago: "anual")));

        Assert.Equal("modified", result.Verdict);
        Assert.Equal("complete", result.Reliability);
        Assert.Equal("word", result.Granularity);
        var change = Assert.Single(result.Changes);
        Assert.Equal("replaced", change.Type);
        Assert.Equal("mensual.", change.Original.Text);
        Assert.Equal("anual.", change.Modified.Text);
        Assert.Equal((1, 3), (change.Original.Page, change.Original.Line));
        Assert.Equal((1, 3), (change.Modified.Page, change.Modified.Line));
        Assert.Equal("1. Objeto", change.Section?.Title);
        Assert.True(change.Section?.Approximate);
        // Ocho palabras de contexto a cada lado.
        Assert.Equal("SERVICIOS 1. Objeto El cliente pagara el servicio", change.Context.Before);
        Assert.Equal("2. Precio El importe total es de 1.000", change.Context.After);
        Assert.Empty(change.Tags);
        Assert.False(change.Suspicious);
        Assert.Equal((0, 0, 1, 1, 1), (result.Stats!.Added, result.Stats.Removed, result.Stats.Replaced, result.Stats.UnitsAdded, result.Stats.UnitsRemoved));
        Assert.True(result.Stats.Similarity is > 0.9 and < 1);
    }

    [Fact]
    public async Task MoneyChange_IsTagged()
    {
        var result = await Compare(PdfFixtures.Lines(Contract()), PdfFixtures.Lines(Contract(importe: "1.500")));

        var change = Assert.Single(result.Changes);
        Assert.Equal(("1.000", "1.500"), (change.Original.Text, change.Modified.Text));
        Assert.Contains("money", change.Tags);
        Assert.Contains("number", change.Tags);
        Assert.Equal("2. Precio", change.Section?.Title);
    }

    [Fact]
    public async Task DateChange_IsTagged()
    {
        var result = await Compare(PdfFixtures.Lines(Contract()), PdfFixtures.Lines(Contract(fecha: "15/10/2026")));

        var change = Assert.Single(result.Changes);
        Assert.Contains("date", change.Tags);
        Assert.Equal((1, 6), (change.Modified.Page, change.Modified.Line));
    }

    [Fact]
    public async Task EmailChange_IsTagged()
    {
        var result = await Compare(PdfFixtures.Lines(Contract()), PdfFixtures.Lines(Contract(correo: "pagos@empresa-falsa.com")));

        Assert.Contains("email", Assert.Single(result.Changes).Tags);
    }

    [Fact]
    public async Task ParagraphAdded_OnPageTwo()
    {
        var original = PdfFixtures.Build(PageSpec.Of(Contract()), PageSpec.Of(SecondPage));
        var modified = PdfFixtures.Build(PageSpec.Of(Contract()), PageSpec.Of(
            SecondPage[0], SecondPage[1], "Clausula nueva de penalizacion por retraso.", "Se aplicara un recargo del 5 por ciento.", SecondPage[2], SecondPage[3]));

        var result = await Compare(original, modified);

        var change = Assert.Single(result.Changes);
        Assert.Equal("added", change.Type);
        Assert.Equal("Clausula nueva de penalizacion por retraso. Se aplicara un recargo del 5 por ciento.", change.Modified.Text);
        Assert.Equal((2, 3, 2, 4), (change.Modified.Page, change.Modified.Line, change.Modified.EndPage, change.Modified.EndLine));
        Assert.True(change.Original.InsertionPoint);
        Assert.Equal((2, 2), (change.Original.Page, change.Original.Line));
        Assert.Equal("3. Vigencia", change.Section?.Title);
        Assert.Equal([2], result.Stats!.PagesAffectedModified);
        Assert.Empty(result.Stats.PagesAffectedOriginal);
    }

    [Fact]
    public async Task ParagraphRemoved_OnPageTwo()
    {
        var original = PdfFixtures.Build(PageSpec.Of(Contract()), PageSpec.Of(SecondPage));
        var modified = PdfFixtures.Build(PageSpec.Of(Contract()), PageSpec.Of(SecondPage[0], SecondPage[1], SecondPage[3]));

        var result = await Compare(original, modified);

        var change = Assert.Single(result.Changes);
        Assert.Equal("removed", change.Type);
        Assert.Equal("Puede renovarse por acuerdo de las partes.", change.Original.Text);
        Assert.Equal((2, 3), (change.Original.Page, change.Original.Line));
        Assert.True(change.Modified.InsertionPoint);
        Assert.Equal([2], result.Stats!.PagesAffectedOriginal);
    }

    [Fact]
    public async Task ChangeOnThirdPage_IsLocatedOnThirdPage()
    {
        PageSpec Page(int n, string last) => PageSpec.Of($"Seccion {n}", "Linea uno.", "Linea dos.", last);

        var result = await Compare(
            PdfFixtures.Build(Page(1, "Fin."), Page(2, "Fin."), Page(3, "Importe 100 pesos.")),
            PdfFixtures.Build(Page(1, "Fin."), Page(2, "Fin."), Page(3, "Importe 900 pesos.")));

        var change = Assert.Single(result.Changes);
        Assert.Equal((3, 4), (change.Original.Page, change.Original.Line));
        Assert.Equal((3, 4), (change.Modified.Page, change.Modified.Line));
        Assert.Contains("money", change.Tags);
    }

    [Fact]
    public async Task MetadataOnly_IsSameText_AndReportsMetadata()
    {
        var original = PdfFixtures.Build([PageSpec.Of(Contract())], info => { info.Title = "Contrato"; info.Producer = "Editor A"; });
        var modified = PdfFixtures.Build([PageSpec.Of(Contract())], info => { info.Title = "Contrato v2"; info.Producer = "Editor B"; });

        var result = await Compare(original, modified);

        Assert.NotEqual(result.Files.Original.Sha256, result.Files.Modified.Sha256);
        Assert.Equal("same_text", result.Verdict);
        Assert.Equal("complete", result.Reliability);
        Assert.Empty(result.Changes);
        Assert.Contains(result.MetadataDifferences, d => d.Field == "title" && d.Original == "Contrato" && d.Modified == "Contrato v2");
        Assert.Contains(result.MetadataDifferences, d => d.Field == "producer");
        Assert.Contains(result.Warnings, w => w.Code == "same_text_scope");
        Assert.False(result.Scope.Images);
        Assert.Equal(1, result.Stats!.Similarity);
    }

    [Fact]
    public async Task ReflowedLines_AreSameText()
    {
        var result = await Compare(
            PdfFixtures.Lines("El cliente pagara", "el servicio mensual", "durante un ano."),
            PdfFixtures.Lines("El cliente pagara el servicio", "mensual durante", "un ano."));

        Assert.Equal("same_text", result.Verdict);
    }

    [Fact]
    public async Task HyphenatedLineBreak_IsSameText()
    {
        var result = await Compare(
            PdfFixtures.Lines("Se entrega el docu-", "mento firmado."),
            PdfFixtures.Lines("Se entrega el documento", "firmado."));

        Assert.Equal("same_text", result.Verdict);
    }

    // ---------------------------------------------------------------- extracción incompleta

    [Fact]
    public async Task PartialExtraction_WithoutTextDifferences_IsNotComparable()
    {
        var result = await Compare(
            PdfFixtures.Build(PageSpec.Of(Contract())),
            PdfFixtures.Build(PageSpec.Of(Contract()), PageSpec.Scanned()));

        Assert.Equal("not_comparable", result.Verdict);
        Assert.Equal("incomplete_extraction", result.NotComparableReason);
        Assert.Equal("partial", result.Reliability);
        Assert.Equal("partial", result.Files.Modified.Extraction?.Status);
        Assert.Equal([2], result.Files.Modified.Extraction?.SuspectedScannedPages);
        Assert.Contains(result.Warnings, w => w.Code == "suspected_scanned_pages" && w.File == "modified");
    }

    [Fact]
    public async Task PartialExtraction_WithTextDifferences_IsModifiedButFlagged()
    {
        var result = await Compare(
            PdfFixtures.Build(PageSpec.Of(Contract())),
            PdfFixtures.Build(PageSpec.Of(Contract(pago: "anual")), PageSpec.Scanned()));

        Assert.Equal("modified", result.Verdict);
        Assert.Equal("partial", result.Reliability);
        Assert.Contains(result.Warnings, w => w.Code == "changes_may_be_artifacts");
        Assert.False(Assert.Single(result.Changes).OnIncompletePage);
    }

    [Fact]
    public async Task ChangeOnScannedPage_IsMarkedAsIncomplete()
    {
        var result = await Compare(
            PdfFixtures.Build(PageSpec.Of(Contract()), PageSpec.Scanned("Firma A")),
            PdfFixtures.Build(PageSpec.Of(Contract()), PageSpec.Scanned("Firma B")));

        Assert.Equal("modified", result.Verdict);
        Assert.True(Assert.Single(result.Changes).OnIncompletePage);
    }

    [Fact]
    public async Task ScannedDocument_WithoutText_IsNotComparable()
    {
        var result = await Compare(PdfFixtures.Lines(Contract()), PdfFixtures.Build(PageSpec.Scanned(), PageSpec.Scanned()));

        Assert.Equal("not_comparable", result.Verdict);
        Assert.Equal("no_text", result.NotComparableReason);
        Assert.Equal("none", result.Files.Modified.Extraction?.Status);
        Assert.Contains(result.Warnings, w => w.Code == "no_text" && w.Severity == "blocking" && w.File == "modified");
    }

    // ---------------------------------------------------------------- PDF que no se pueden leer

    [Fact]
    public async Task EncryptedPdf_IsNotComparable_WithoutThrowing()
    {
        var result = await Compare(PdfFixtures.Lines(Contract()), PdfFixtures.EncryptedWithUserPassword());

        Assert.Equal("not_comparable", result.Verdict);
        Assert.Equal("encrypted", result.NotComparableReason);
        Assert.True(result.Files.Modified.Extraction?.Encrypted);
        Assert.Equal("complete", result.Files.Original.Extraction?.Status);
        Assert.Contains(result.Warnings, w => w.Code == "encrypted" && w.File == "modified");
    }

    [Fact]
    public async Task DamagedPdf_IsUnreadable_WithoutThrowing()
    {
        var result = await Compare(PdfFixtures.Garbage(), PdfFixtures.Lines(Contract()));

        Assert.Equal("not_comparable", result.Verdict);
        Assert.Equal("unreadable", result.NotComparableReason);
        Assert.Contains(result.Warnings, w => w.Code == "unreadable" && w.File == "original");
    }

    [Fact]
    public async Task TruncatedPdf_NeverThrows()
    {
        var valid = PdfFixtures.Build(PageSpec.Of(Contract()), PageSpec.Of(SecondPage));

        foreach (var fraction in new[] { 0.2, 0.5, 0.8, 0.95 })
        {
            var truncated = valid[..(int)(valid.Length * fraction)];

            var result = await Compare(PdfFixtures.Lines(Contract()), truncated);

            Assert.Contains(result.Verdict, new[] { "not_comparable", "modified", "same_text" });
        }
    }

    [Fact]
    public async Task BothDocumentsBlocked_ReportsBoth()
    {
        var result = await Compare(PdfFixtures.EncryptedWithUserPassword(), PdfFixtures.Garbage());

        Assert.Equal("encrypted", result.NotComparableReason);
        Assert.Contains(result.Warnings, w => w.Code == "encrypted" && w.File == "original");
        Assert.Contains(result.Warnings, w => w.Code == "unreadable" && w.File == "modified");
    }

    // ---------------------------------------------------------------- límites

    [Fact]
    public async Task MoreThanMaxPages_IsNotComparable()
    {
        var result = await Compare(PdfFixtures.ManyPages(201), PdfFixtures.ManyPages(1));

        Assert.Equal("not_comparable", result.Verdict);
        Assert.Equal("too_many_pages", result.NotComparableReason);
        Assert.Equal(201, result.Files.Original.PageCount);
    }

    [Fact]
    public async Task MaxPages_IsAccepted_AndLastPageChangeIsLocated()
    {
        var original = PdfFixtures.ManyPages(200);
        var modified = PdfFixtures.Build(Enumerable.Range(1, 200)
            .Select(i => PageSpec.Of(i == 200 ? "Pagina 200 del documento modificado." : $"Pagina {i} del documento de prueba.")));

        var result = await Compare(original, modified);

        Assert.Equal("modified", result.Verdict);
        var change = Assert.Single(result.Changes);
        Assert.Equal(200, change.Original.Page);
        Assert.Equal(200, change.Modified.Page);
    }

    [Fact]
    public async Task TooMuchText_IsNotComparable()
    {
        var result = await Compare(
            PdfFixtures.RandomText(1, 2, 20, 10),
            PdfFixtures.Lines("corto"),
            new ComparisonOptions { MaxWordsPerDocument = 100 });

        Assert.Equal("too_much_text", result.NotComparableReason);
    }

    [Fact]
    public async Task ManyChanges_FallBackToLines()
    {
        var result = await Compare(
            PdfFixtures.RandomText(1, 10, 40, 10),
            PdfFixtures.RandomText(2, 10, 40, 10),
            new ComparisonOptions { MaxDiffOperations = 5_000_000 });

        Assert.Equal("modified", result.Verdict);
        Assert.Equal("line", result.Granularity);
        Assert.Contains(result.Warnings, w => w.Code == "coarse_granularity");
        Assert.NotEmpty(result.Changes);
    }

    [Fact]
    public async Task TooManyDifferences_AreReportedWithoutDetail()
    {
        var result = await Compare(
            PdfFixtures.RandomText(1, 10, 40, 10),
            PdfFixtures.RandomText(2, 10, 40, 10),
            new ComparisonOptions { MaxDiffOperations = 100 });

        Assert.Equal("modified", result.Verdict);
        Assert.True(result.DiffIncomplete);
        Assert.Equal("none", result.Granularity);
        Assert.Empty(result.Changes);
        Assert.Contains(result.Warnings, w => w.Code == "too_many_differences");
    }

    [Fact]
    public async Task LargeNumberOfChanges_IsTruncatedInResponse()
    {
        var original = PdfFixtures.Lines(Enumerable.Range(1, 12).Select(i => $"Linea {i} con texto estable numero {i}.").ToArray());
        var modified = PdfFixtures.Lines(Enumerable.Range(1, 12).Select(i => $"Linea {i} con texto {(i % 2 == 0 ? "cambiado" : "estable")} numero {i}.").ToArray());

        var result = await Compare(original, modified, new ComparisonOptions { MaxChangesInResponse = 3 });

        Assert.Equal(6, result.TotalChanges);
        Assert.Equal(3, result.Changes.Count);
        Assert.True(result.ChangesTruncated);
        Assert.Contains(result.Warnings, w => w.Code == "changes_truncated");
    }

    [Fact]
    public async Task LongChangeText_IsTruncated()
    {
        var longLine = string.Join(' ', Enumerable.Repeat("palabra", 300));

        var result = await Compare(PdfFixtures.Lines("Inicio.", "Fin."), PdfFixtures.Lines("Inicio.", longLine, "Fin."),
            new ComparisonOptions { MaxChangeTextLength = 100 });

        var change = Assert.Single(result.Changes);
        Assert.True(change.Modified.TextTruncated);
        Assert.Equal(100, change.Modified.Text.Length);
    }

    // ---------------------------------------------------------------- determinismo y tiempo

    [Fact]
    public async Task SameInput_ProducesSameResult()
    {
        var original = PdfFixtures.Build(PageSpec.Of(Contract()), PageSpec.Of(SecondPage));
        var modified = PdfFixtures.Build(PageSpec.Of(Contract(importe: "2.000", fecha: "01/11/2026")), PageSpec.Of(SecondPage[0], SecondPage[2]));

        string Json(ComparisonResultDto r)
        {
            r.ComparedAt = default;
            return JsonSerializer.Serialize(r);
        }

        var first = Json(await Compare(original, modified));
        var second = Json(await Compare(original, modified));

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ExtractionTimeout_IsNotComparable_AndHoldsTurnUntilWorkEnds()
    {
        var options = new ComparisonOptions { ExtractionTimeoutSeconds = 0.2, ExtractionGraceSeconds = 0.1, MaxConcurrentComparisons = 1 };
        var gate = new ComparisonGate(Options.Create(options));
        var extractor = new BlockingExtractor(TimeSpan.FromMilliseconds(1500));
        var lease = await gate.TryEnterAsync(TimeSpan.Zero, CancellationToken.None);

        var result = await Service(options, extractor).CompareAsync(
            new("a.pdf", PdfFixtures.Lines("a")), new("b.pdf", PdfFixtures.Lines("b")), lease, CancellationToken.None);
        lease!.Dispose();

        Assert.Equal("not_comparable", result.Verdict);
        Assert.Equal("timeout", result.NotComparableReason);
        Assert.Equal(0, gate.AvailableSlots);

        await extractor.Finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(200);
        Assert.Equal(1, gate.AvailableSlots);
    }

    [Fact]
    public async Task ExtractorObservesTimeoutBetweenPages()
    {
        var result = await Compare(PdfFixtures.ManyPages(150), PdfFixtures.ManyPages(149),
            new ComparisonOptions { ExtractionTimeoutSeconds = 0.001 });

        Assert.Equal("not_comparable", result.Verdict);
        Assert.Equal("timeout", result.NotComparableReason);
    }

    // Extractor que ignora la cancelación: simula una página que PdfPig no termina de leer.
    // Finished se completa cuando termina de "extraer" los dos documentos.
    private sealed class BlockingExtractor(TimeSpan duration) : IPdfPageExtractor
    {
        private int _calls;

        public TaskCompletionSource Finished { get; } = new();

        public ExtractedDocument Extract(byte[] pdf, CancellationToken cancellationToken)
        {
            Thread.Sleep(duration);

            if (Interlocked.Increment(ref _calls) == 2)
            {
                Finished.TrySetResult();
            }

            return new ExtractedDocument { Status = ExtractionStatus.Complete };
        }
    }
}

public class PdfPageExtractorTests
{
    private static ExtractedDocument Extract(byte[] pdf) =>
        new PdfPageExtractor(Options.Create(new ComparisonOptions()), NullLogger<PdfPageExtractor>.Instance)
            .Extract(pdf, CancellationToken.None);

    [Fact]
    public void LinesKeepPageAndLineNumbers()
    {
        var document = Extract(PdfFixtures.Build(PageSpec.Of("Primera linea", "Segunda linea"), PageSpec.Of("Otra pagina")));

        Assert.Equal(ExtractionStatus.Complete, document.Status);
        Assert.Equal(2, document.PageCount);
        Assert.Equal(
            [(1, 1, "Primera linea"), (1, 2, "Segunda linea"), (2, 1, "Otra pagina")],
            document.Lines.Select(l => (l.Page, l.Line, l.Text)));
    }

    [Fact]
    public void BlankPage_DoesNotMakeExtractionPartial()
    {
        var document = Extract(PdfFixtures.Build(PageSpec.Of("Texto"), PageSpec.Of()));

        Assert.Equal(ExtractionStatus.Complete, document.Status);
        Assert.Equal(PageStatus.Blank, document.Pages[1].Status);
    }

    [Fact]
    public void ScannedPage_IsDetected()
    {
        var document = Extract(PdfFixtures.Build(PageSpec.Of("Texto"), PageSpec.Scanned()));

        Assert.Equal(ExtractionStatus.Partial, document.Status);
        Assert.Equal(PageStatus.SuspectedScanned, document.Pages[1].Status);
        Assert.Equal(1, document.Pages[1].Images);
    }

    [Fact]
    public void Metadata_IsRead()
    {
        var document = Extract(PdfFixtures.Build([PageSpec.Of("Texto")], info => info.Title = "Titulo ficticio"));

        Assert.Equal("Titulo ficticio", document.Metadata["title"]);
    }

    [Fact]
    public void EncryptedPdf_IsReportedNotThrown() =>
        Assert.Equal(ExtractionStatus.Encrypted, Extract(PdfFixtures.EncryptedWithUserPassword()).Status);

    [Fact]
    public void GarbagePdf_IsReportedNotThrown() =>
        Assert.Equal(ExtractionStatus.Unreadable, Extract(PdfFixtures.Garbage()).Status);
}
