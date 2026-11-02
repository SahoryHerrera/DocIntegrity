using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DocIntegrity.Api.Services.Comparison;

// Etiquetas deterministas de un cambio, calculadas solo con su texto y el contexto
// inmediato. No usan IA. "Sospechosas": invisible, lookalike y mixed_script.
public static partial class ChangeTagger
{
    public static readonly IReadOnlySet<string> SuspiciousTags =
        new HashSet<string> { "invisible", "lookalike", "mixed_script" };

    [GeneratedRegex(@"[$€£¥]|\b(USD|EUR|MXN|COP|ARS|CLP|PEN|GBP|pesos?|euros?|d[oó]lar(es)?|centavos?|c[eé]ntimos?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyRegex();

    [GeneratedRegex(@"\b\d{1,2}[/.\-]\d{1,2}[/.\-]\d{2,4}\b|\b\d{4}-\d{2}-\d{2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex NumericDateRegex();

    [GeneratedRegex(@"\b(enero|febrero|marzo|abril|mayo|junio|julio|agosto|septiembre|setiembre|octubre|noviembre|diciembre|january|february|march|april|june|july|august|september|october|november|december)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MonthRegex();

    [GeneratedRegex(@"[\w.+\-]+@[\w\-]+(\.[\w\-]+)+", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    // Letras cirílicas y griegas que se ven igual que letras latinas.
    private static readonly Dictionary<char, char> Confusables = new()
    {
        ['\u0430'] = 'a', ['\u0435'] = 'e', ['\u043E'] = 'o', ['\u0440'] = 'p', ['\u0441'] = 'c', ['\u0443'] = 'y', ['\u0445'] = 'x',
        ['\u0456'] = 'i', ['\u0458'] = 'j', ['\u0455'] = 's', ['\u0501'] = 'd', ['\u04BB'] = 'h', ['\u0578'] = 'n',
        ['\u0410'] = 'A', ['\u0412'] = 'B', ['\u0415'] = 'E', ['\u041A'] = 'K', ['\u041C'] = 'M', ['\u041D'] = 'H', ['\u041E'] = 'O',
        ['\u0420'] = 'P', ['\u0421'] = 'C', ['\u0422'] = 'T', ['\u0425'] = 'X', ['\u0406'] = 'I', ['\u0408'] = 'J', ['\u0405'] = 'S',
        ['\u03BF'] = 'o', ['\u03B1'] = 'a', ['\u03BD'] = 'v', ['\u03B9'] = 'i', ['\u03BA'] = 'k', ['\u03C4'] = 't',
        ['\u0391'] = 'A', ['\u0392'] = 'B', ['\u0395'] = 'E', ['\u0396'] = 'Z', ['\u0397'] = 'H', ['\u0399'] = 'I', ['\u039A'] = 'K',
        ['\u039C'] = 'M', ['\u039D'] = 'N', ['\u039F'] = 'O', ['\u03A1'] = 'P', ['\u03A4'] = 'T', ['\u03A5'] = 'Y', ['\u03A7'] = 'X',
    };

    public static List<string> Tag(string original, string modified, string contextBefore, string contextAfter)
    {
        var tags = new List<string>();
        var changed = original + " " + modified;
        // Dos palabras de contexto a cada lado: "1.000 → 1.500" seguido de "euros".
        var near = LastWords(contextBefore, 2) + " " + changed + " " + FirstWords(contextAfter, 2);
        var hasNumber = changed.Any(char.IsDigit);

        if (hasNumber)
        {
            tags.Add("number");
        }

        if (CurrencyRegex().IsMatch(changed) || (hasNumber && CurrencyRegex().IsMatch(near)))
        {
            tags.Add("money");
        }

        if (NumericDateRegex().IsMatch(changed) || MonthRegex().IsMatch(changed) || (hasNumber && MonthRegex().IsMatch(near)))
        {
            tags.Add("date");
        }

        if (EmailRegex().IsMatch(changed))
        {
            tags.Add("email");
        }

        if (changed.Any(c => char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format))
        {
            tags.Add("invisible");
        }

        if (original.Length > 0 && modified.Length > 0 && original != modified)
        {
            if (Fold(original) == Fold(modified))
            {
                tags.Add("lookalike");
            }

            if (original.ToLowerInvariant() == modified.ToLowerInvariant())
            {
                tags.Add("case_only");
            }
            else if (StripPunctuation(original) == StripPunctuation(modified))
            {
                tags.Add("punctuation_only");
            }
        }

        if (HasMixedScript(original) || HasMixedScript(modified))
        {
            tags.Add("mixed_script");
        }

        return tags;
    }

    // NFKC (ancho completo, superíndices…) + letras confundibles → latinas, sin invisibles.
    private static string Fold(string text)
    {
        string normalized;

        try
        {
            normalized = text.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            normalized = text;
        }

        var builder = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            if (char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format)
            {
                continue;
            }

            builder.Append(Confusables.TryGetValue(c, out var latin) ? latin : c);
        }

        return builder.ToString();
    }

    private static string StripPunctuation(string text) =>
        new(text.Where(c => !char.IsPunctuation(c) && !char.IsSymbol(c) && !char.IsWhiteSpace(c)).ToArray());

    // Una misma palabra con letras latinas y cirílicas o griegas.
    private static bool HasMixedScript(string text)
    {
        foreach (var word in TextNormalizer.SplitWords(text))
        {
            bool latin = false, other = false;

            foreach (var c in word)
            {
                if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '\u00C0' and <= '\u024F')
                {
                    latin = true;
                }
                else if (c is >= '\u0370' and <= '\u03FF' or >= '\u0400' and <= '\u052F')
                {
                    other = true;
                }
            }

            if (latin && other)
            {
                return true;
            }
        }

        return false;
    }

    private static string FirstWords(string text, int count) =>
        string.Join(' ', TextNormalizer.SplitWords(text).Take(count));

    private static string LastWords(string text, int count) =>
        string.Join(' ', TextNormalizer.SplitWords(text).TakeLast(count));
}
