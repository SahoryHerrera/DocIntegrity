using DocIntegrity.Api.Services.Comparison;
using Microsoft.Extensions.Options;

namespace DocIntegrity.Api.Tests;

// Caracteres especiales por su código, para que la prueba muestre qué carácter usa.
internal static class Chars
{
    public static string U(int code) => char.ConvertFromUtf32(code);

    public static readonly string SoftHyphen = U(0x00AD);
    public static readonly string LigatureFi = U(0xFB01);
    public static readonly string CombiningAcute = U(0x0301);
    public static readonly string ZeroWidthSpace = U(0x200B);
    public static readonly string NoBreakSpace = U(0x00A0);
    public static readonly string CyrillicSmallA = U(0x0430);
    public static readonly string CyrillicSmallO = U(0x043E);
    public static readonly string ReplacementChar = U(0xFFFD);
    public static readonly string PrivateUse = U(0xE000);
}

public class TextNormalizerTests
{
    private static List<string> Words(params string[] lines) =>
        TextNormalizer.ToWords(lines.Select((text, i) => new ExtractedLine(1, i + 1, text))).Select(w => w.Text).ToList();

    [Fact]
    public void Nfc_UnifiesCombiningAccent() =>
        Assert.Equal(Words("caf" + "e" + Chars.CombiningAcute), Words("caf" + Chars.U(0x00E9)));

    [Fact]
    public void Ligatures_AreExpanded() =>
        Assert.Equal(["firma"], Words(Chars.LigatureFi + "rma"));

    [Fact]
    public void SoftHyphen_IsRemoved() =>
        Assert.Equal(["documento"], Words("docu" + Chars.SoftHyphen + "mento"));

    [Fact]
    public void Whitespace_AmountAndTypeDoNotMatter() =>
        Assert.Equal(Words("uno dos tres"), Words("  uno\t\tdos" + Chars.NoBreakSpace + "  tres  "));

    [Fact]
    public void LineHyphenation_JoinsLowercaseParts() =>
        Assert.Equal(["el", "documento", "firmado"], Words("el docu-", "mento firmado"));

    [Fact]
    public void LineHyphenation_KeepsCompoundsBeforeUppercase() =>
        Assert.Equal(["franco-", "Aleman"], Words("franco-", "Aleman"));

    [Fact]
    public void LineHyphenation_KeepsStandaloneDash() =>
        Assert.Equal(["precio", "-", "total"], Words("precio -", "total"));

    [Fact]
    public void LineHyphenation_WorksAcrossPages()
    {
        var words = TextNormalizer.ToWords([new ExtractedLine(1, 9, "la clau-"), new ExtractedLine(2, 1, "sula final")]);

        Assert.Equal(["la", "clausula", "final"], words.Select(w => w.Text));
        Assert.Equal(1, words[1].Page);
    }

    [Fact]
    public void CaseAndPunctuation_AreKept() =>
        Assert.NotEqual(Words("Total: 100."), Words("total 100"));

    [Fact]
    public void InvisibleCharacters_AreKept() =>
        Assert.NotEqual(Words("pago"), Words("pa" + Chars.ZeroWidthSpace + "go"));

    [Fact]
    public void Words_KeepPageAndLine()
    {
        var words = TextNormalizer.ToWords([new ExtractedLine(3, 7, "hola mundo")]);

        Assert.All(words, w => Assert.Equal((3, 7), (w.Page, w.Line)));
    }
}

public class ChangeTaggerTests
{
    private static List<string> Tags(string original, string modified, string before = "", string after = "") =>
        ChangeTagger.Tag(original, modified, before, after);

    [Fact]
    public void PlainWords_HaveNoTags() => Assert.Empty(Tags("mensual", "anual"));

    [Fact]
    public void Money_WithCurrencyInContext() =>
        Assert.Equal(["number", "money"], Tags("1.000", "1.500", "importe total de", "euros pagaderos"));

    [Fact]
    public void Money_WithSymbol() => Assert.Contains("money", Tags("$1,000", "$1,500"));

    [Fact]
    public void NumberWithoutCurrency_IsNotMoney() => Assert.Equal(["number"], Tags("10", "12", "plazo de", "dias habiles"));

    [Fact]
    public void Date_Numeric() => Assert.Contains("date", Tags("12/10/2026", "15/10/2026"));

    [Fact]
    public void Date_Iso() => Assert.Contains("date", Tags("2026-10-12", "2026-10-15"));

    [Fact]
    public void Date_DayChangedBeforeMonthName() => Assert.Contains("date", Tags("12", "15", "firmado el", "de octubre de 2026"));

    [Fact]
    public void Email() => Assert.Contains("email", Tags("pagos@empresa.com", "pagos@empresa-falsa.com"));

    [Fact]
    public void Invisible_ZeroWidthSpace()
    {
        var tags = Tags("pago", "pa" + Chars.ZeroWidthSpace + "go");

        Assert.Contains("invisible", tags);
        Assert.Contains("lookalike", tags);
    }

    [Fact]
    public void Lookalike_CyrillicLetter()
    {
        var tags = Tags("banco", "banc" + Chars.CyrillicSmallO);

        Assert.Contains("lookalike", tags);
        Assert.Contains("mixed_script", tags);
    }

    [Fact]
    public void MixedScript_InAddedText() => Assert.Contains("mixed_script", Tags("", "cuent" + Chars.CyrillicSmallA));

    [Fact]
    public void CaseOnly() => Assert.Equal(["case_only"], Tags("Contrato", "CONTRATO"));

    [Fact]
    public void PunctuationOnly() => Assert.Equal(["punctuation_only"], Tags("servicios,", "servicios;"));
}

public class SectionDetectorTests
{
    [Theory]
    [InlineData("CLAUSULA TERCERA. PRECIO")]
    [InlineData("Clausula tercera")]
    [InlineData("Articulo 5")]
    [InlineData("Clausula 3.")]
    [InlineData("3. Obligaciones del cliente")]
    [InlineData("2.1 Forma de pago")]
    [InlineData("IV. Vigencia")]
    [InlineData("CONDICIONES GENERALES")]
    public void Headings(string text) => Assert.True(SectionDetector.IsHeading(text));

    [Theory]
    [InlineData("El cliente pagara el servicio mensual.")]
    [InlineData("1. El cliente acepta las condiciones.")]
    [InlineData("Clausula nueva de penalizacion por retraso.")]
    [InlineData("ok")]
    [InlineData("12345")]
    [InlineData("ESTE TEXTO EN MAYUSCULAS ES DEMASIADO LARGO PARA SER UN TITULO PORQUE TIENE MUCHAS MAS PALABRAS DE LAS PERMITIDAS")]
    public void NotHeadings(string text) => Assert.False(SectionDetector.IsHeading(text));

    [Fact]
    public void Find_ReturnsLastHeadingBefore()
    {
        var headings = SectionDetector.Detect(
        [
            new ExtractedLine(1, 1, "1. Objeto"),
            new ExtractedLine(1, 2, "texto del objeto"),
            new ExtractedLine(2, 1, "2. Precio"),
            new ExtractedLine(2, 2, "texto del precio")
        ]);

        Assert.Equal("1. Objeto", SectionDetector.Find(headings, 1, 2)?.Title);
        Assert.Equal("2. Precio", SectionDetector.Find(headings, 2, 5)?.Title);
        Assert.Equal("2. Precio", SectionDetector.Find(headings, 3, 1)?.Title);
        Assert.Null(SectionDetector.Find(headings, 0, 9));
    }
}

public class PageClassificationTests
{
    [Fact]
    public void TextPage() => Assert.Equal(PageStatus.Text, PdfPageExtractor.ClassifyPage("Texto normal de la pagina.", 0, 0).Status);

    [Fact]
    public void BlankPage() => Assert.Equal(PageStatus.Blank, PdfPageExtractor.ClassifyPage("  \n ", 0, 0).Status);

    [Fact]
    public void ImageWithoutText_IsScanned() => Assert.Equal(PageStatus.SuspectedScanned, PdfPageExtractor.ClassifyPage("", 1, 0.1).Status);

    [Fact]
    public void FullPageImageWithFewChars_IsScanned() => Assert.Equal(PageStatus.SuspectedScanned, PdfPageExtractor.ClassifyPage("Firma", 1, 0.9).Status);

    [Fact]
    public void FullPageImageWithRealText_IsText() =>
        Assert.Equal(PageStatus.Text, PdfPageExtractor.ClassifyPage(new string('a', 400), 1, 1.0).Status);

    [Fact]
    public void ReplacementCharacters_AreGarbled() =>
        Assert.Equal(PageStatus.Garbled, PdfPageExtractor.ClassifyPage(string.Concat(Enumerable.Repeat(Chars.ReplacementChar + "ab", 20)), 0, 0).Status);

    [Fact]
    public void PrivateUseGlyphs_AreGarbled() =>
        Assert.Equal(PageStatus.Garbled, PdfPageExtractor.ClassifyPage(string.Concat(Enumerable.Repeat(Chars.PrivateUse, 30)), 0, 0).Status);

    [Fact]
    public void FewUnreadableChars_AreTolerated() =>
        Assert.Equal(PageStatus.Text, PdfPageExtractor.ClassifyPage(new string('a', 100) + Chars.ReplacementChar, 0, 0).Status);
}

public class ComparisonGateTests
{
    private static ComparisonGate Gate(int slots) =>
        new(Options.Create(new ComparisonOptions { MaxConcurrentComparisons = slots }));

    [Fact]
    public async Task ThirdComparison_IsRejectedWhileTwoRun()
    {
        var gate = Gate(2);

        using var first = await gate.TryEnterAsync(TimeSpan.Zero, CancellationToken.None);
        using var second = await gate.TryEnterAsync(TimeSpan.Zero, CancellationToken.None);
        var third = await gate.TryEnterAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Null(third);
    }

    [Fact]
    public async Task ReleasedTurn_IsAvailableAgain()
    {
        var gate = Gate(1);

        (await gate.TryEnterAsync(TimeSpan.Zero, CancellationToken.None))!.Dispose();

        using var again = await gate.TryEnterAsync(TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(again);
    }

    [Fact]
    public async Task WaitingComparison_GetsTurnWhenOneFinishes()
    {
        var gate = Gate(1);
        var first = await gate.TryEnterAsync(TimeSpan.Zero, CancellationToken.None);

        var waiting = gate.TryEnterAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        first!.Dispose();

        using var lease = await waiting;
        Assert.NotNull(lease);
    }

    [Fact]
    public async Task HeldTurn_IsReleasedOnlyWhenWorkFinishes()
    {
        var gate = Gate(1);
        var work = new TaskCompletionSource();

        var lease = await gate.TryEnterAsync(TimeSpan.Zero, CancellationToken.None);
        lease!.HoldUntil(work.Task);
        lease.Dispose();

        Assert.Equal(0, gate.AvailableSlots);

        work.SetResult();
        await Task.Delay(100);

        Assert.Equal(1, gate.AvailableSlots);
    }

    [Fact]
    public async Task DoubleDispose_ReleasesOnce()
    {
        var gate = Gate(2);
        var lease = await gate.TryEnterAsync(TimeSpan.Zero, CancellationToken.None);

        lease!.Dispose();
        lease.Dispose();

        Assert.Equal(2, gate.AvailableSlots);
    }
}
