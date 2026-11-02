using System.Text;

namespace DocIntegrity.Api.Services.Comparison;

// Unidad comparable: una palabra (o una línea, en el modo de respaldo) y dónde está.
public sealed record TextUnit(string Text, int Page, int Line);

public sealed record NormalizationStep(string Code, string Description);

// Normalización determinista del texto extraído. Solo elimina diferencias que
// no son del texto (codificación, maquetación). NO ignora mayúsculas,
// puntuación, números ni caracteres invisibles: esos sí son cambios.
public static class TextNormalizer
{
    public static readonly IReadOnlyList<NormalizationStep> Steps =
    [
        new("unicode_nfc", "Forma Unicode NFC: \"é\" compuesta y \"e\" + acento combinado se tratan igual."),
        new("ligatures", "Ligaduras tipográficas (ﬁ, ﬂ, ﬀ, ﬃ, ﬄ, ﬅ, ﬆ) se escriben con sus letras."),
        new("soft_hyphen", "Se eliminan los guiones blandos (U+00AD), que solo indican dónde puede cortarse una palabra."),
        new("whitespace", "Cualquier secuencia de espacios, tabulaciones o saltos de línea separa palabras; su cantidad no cuenta como cambio."),
        new("line_hyphenation", "Una palabra cortada con guion al final de una línea (\"docu-\" + \"mento\") se une si ambas partes son minúsculas."),
    ];

    private static readonly Dictionary<char, string> Ligatures = new()
    {
        ['\uFB00'] = "ff", ['\uFB01'] = "fi", ['\uFB02'] = "fl", ['\uFB03'] = "ffi",
        ['\uFB04'] = "ffl", ['\uFB05'] = "st", ['\uFB06'] = "st",
    };

    public static string NormalizeLine(string raw)
    {
        string text;

        try
        {
            text = raw.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            // Texto con sustitutos Unicode inválidos: se compara tal cual.
            text = raw;
        }

        var builder = new StringBuilder(text.Length);

        foreach (var c in text)
        {
            if (c == '\u00AD')
            {
                continue;
            }

            if (Ligatures.TryGetValue(c, out var expanded))
            {
                builder.Append(expanded);
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    public static IEnumerable<string> SplitWords(string normalizedLine)
    {
        var start = -1;

        for (var i = 0; i <= normalizedLine.Length; i++)
        {
            var separator = i == normalizedLine.Length || char.IsWhiteSpace(normalizedLine[i]);

            if (separator && start >= 0)
            {
                yield return normalizedLine[start..i];
                start = -1;
            }
            else if (!separator && start < 0)
            {
                start = i;
            }
        }
    }

    // Palabras de todo el documento, en orden, con su página y línea de origen.
    public static List<TextUnit> ToWords(IEnumerable<ExtractedLine> lines)
    {
        var words = new List<TextUnit>();
        var previousLineEndsWithHyphen = false;

        foreach (var line in lines)
        {
            var lineWords = SplitWords(NormalizeLine(line.Text)).ToList();

            if (lineWords.Count == 0)
            {
                continue;
            }

            var first = 0;

            if (previousLineEndsWithHyphen && char.IsLower(lineWords[0][0]))
            {
                var last = words[^1];
                words[^1] = last with { Text = last.Text[..^1] + lineWords[0] };
                first = 1;
            }

            for (var i = first; i < lineWords.Count; i++)
            {
                words.Add(new TextUnit(lineWords[i], line.Page, line.Line));
            }

            previousLineEndsWithHyphen = EndsWithBreakHyphen(words[^1].Text);
        }

        return words;
    }

    // Líneas normalizadas (palabras unidas con un espacio): unidad del modo de respaldo.
    public static List<TextUnit> ToLines(IEnumerable<ExtractedLine> lines) =>
        lines
            .Select(line => new TextUnit(string.Join(' ', SplitWords(NormalizeLine(line.Text))), line.Page, line.Line))
            .Where(unit => unit.Text.Length > 0)
            .ToList();

    // "docu-": guion ASCII o U+2010 precedido de una letra minúscula.
    private static bool EndsWithBreakHyphen(string word) =>
        word.Length >= 2 &&
        word[^1] is '-' or '\u2010' &&
        char.IsLower(word[^2]);
}
