using System.Text.RegularExpressions;

namespace DocIntegrity.Api.Services.Comparison;

public sealed record SectionHeading(int Page, int Line, string Title);

// Encabezados probables, detectados solo por el texto de cada línea (PdfPig no
// indica la estructura). El resultado es siempre aproximado.
public static partial class SectionDetector
{
    private const int MaxTitleLength = 90;
    private const int MaxTitleWords = 12;

    [GeneratedRegex(@"^(CAP[IÍ]TULO|CL[AÁ]USULA|ART[IÍ]CULO|SECCI[OÓ]N|ANEXO|T[IÍ]TULO|APARTADO|CHAPTER|SECTION|ARTICLE)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeywordRegex();

    // "1.", "2.3 ", "IV. " seguidos de una mayúscula.
    [GeneratedRegex(@"^(\d{1,3}(\.\d{1,3})*\.?|[IVXLC]{1,6}\.)\s+\p{Lu}", RegexOptions.CultureInvariant)]
    private static partial Regex NumberedRegex();

    public static List<SectionHeading> Detect(IEnumerable<ExtractedLine> lines)
    {
        var headings = new List<SectionHeading>();

        foreach (var line in lines)
        {
            var text = string.Join(' ', TextNormalizer.SplitWords(TextNormalizer.NormalizeLine(line.Text)));

            if (IsHeading(text))
            {
                headings.Add(new SectionHeading(line.Page, line.Line, text));
            }
        }

        return headings;
    }

    public static bool IsHeading(string text)
    {
        if (text.Length < 3 || text.Length > MaxTitleLength ||
            TextNormalizer.SplitWords(text).Count() > MaxTitleWords)
        {
            return false;
        }

        // Una línea de más de 3 palabras que termina en punto es una frase, no un título
        // ("Clausula nueva de penalizacion por retraso."). "Cláusula 3." sí lo es.
        var looksLikeSentence = text.EndsWith('.') && TextNormalizer.SplitWords(text).Count() > 3;

        if (KeywordRegex().IsMatch(text) && !looksLikeSentence)
        {
            return true;
        }

        // Numerado y sin punto final (una frase numerada de una lista suele terminar en punto).
        if (NumberedRegex().IsMatch(text) && !text.EndsWith('.'))
        {
            return true;
        }

        // Todo en mayúsculas, con al menos 4 letras.
        var letters = text.Where(char.IsLetter).ToList();

        return letters.Count >= 4 && letters.All(char.IsUpper);
    }

    // Último encabezado en (page, line) o antes.
    public static SectionHeading? Find(IReadOnlyList<SectionHeading> headings, int page, int line)
    {
        SectionHeading? found = null;

        foreach (var heading in headings)
        {
            if (heading.Page < page || (heading.Page == page && heading.Line <= line))
            {
                found = heading;
            }
            else
            {
                break;
            }
        }

        return found;
    }
}
