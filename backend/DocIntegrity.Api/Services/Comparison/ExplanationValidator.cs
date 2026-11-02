using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocIntegrity.Api.DTOs;

namespace DocIntegrity.Api.Services.Comparison;

public sealed class ExplanationFormatException(string message) : Exception(message);

// Valida la respuesta de la IA contra los cambios deterministas:
//   - descarta los ids que no existen (o que no se enviaron);
//   - recorta longitudes y caracteres de control;
//   - avisa de cifras o correos que no aparecen en los cambios y de afirmaciones que contradicen el resultado.
// Nunca modifica el veredicto ni los cambios: solo filtra y anota la interpretación.
public static partial class ExplanationValidator
{
    private static readonly HashSet<string> Relevances = ["alta", "media", "baja"];

    // Cifras: 12.500,00 · 15/11/2026 · 5 · 10:30. No cuenta los dígitos de los ids ("c3").
    [GeneratedRegex(@"(?<![\p{L}\d])\d+(?:[.,:/\-]\d+)*", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"[\w.+\-]+@[\w\-]+(\.[\w\-]+)+", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    // Decimales finales de un importe: «,00» o «.50».
    [GeneratedRegex(@"[.,]\d{1,2}$", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalsRegex();

    // "no hay cambios", "sin cambios", "son idénticos", "no se detectaron/encontraron diferencias"…
    [GeneratedRegex(@"\b(no\s+hay\s+(ning[uú]n\s+)?(cambios?|diferencias?|modificaci[oó]n(es)?)|sin\s+(cambios|diferencias|modificaciones)|son\s+id[eé]nticos|no\s+se\s+(detect|encontr)\w*\s+(ning[uú]n\w*\s+)?(cambios?|diferencias?))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NoChangesRegex();

    public static ComparisonExplanationDto Validate(
        string content,
        ExplanationPromptData prompt,
        ComparisonResultDto result,
        ExplanationOptions options)
    {
        JsonElement root;

        try
        {
            using var document = JsonDocument.Parse(content);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new ExplanationFormatException("La respuesta no es JSON válido.");
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ExplanationFormatException("La respuesta no es un objeto JSON.");
        }

        var summary = Clean(root.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null, options.MaxSummaryChars);
        var sentIds = prompt.Changes.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var explanation = new ComparisonExplanationDto
        {
            Summary = summary,
            ChangesSent = prompt.Changes.Count,
            ChangesOmitted = prompt.Omitted
        };

        if (root.TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in changes.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var id = item.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String ? idElement.GetString()!.Trim() : string.Empty;
                var text = Clean(item.TryGetProperty("explanation", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null, options.MaxExplanationChars);
                var relevance = (item.TryGetProperty("relevance", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null)?.Trim().ToLowerInvariant();

                if (!sentIds.Contains(id))
                {
                    // Id inventado (o de un cambio que no se envió): se descarta.
                    if (id.Length > 0 && !explanation.DiscardedChangeIds.Contains(id))
                    {
                        explanation.DiscardedChangeIds.Add(Clean(id, 40));
                    }

                    continue;
                }

                if (text.Length == 0 || explanation.Changes.Any(c => c.Id == id))
                {
                    continue;
                }

                explanation.Changes.Add(new ChangeExplanationDto
                {
                    Id = id,
                    Explanation = text,
                    Relevance = relevance is not null && Relevances.Contains(relevance) ? relevance : "media"
                });
            }
        }

        if (explanation.Summary.Length == 0 && explanation.Changes.Count == 0)
        {
            throw new ExplanationFormatException("La respuesta no contiene un resumen ni explicaciones válidas.");
        }

        // Orden del documento, no el que eligió la IA.
        var order = prompt.Changes.Select((c, i) => (c.Id, i)).ToDictionary(x => x.Id, x => x.i);
        explanation.Changes = [.. explanation.Changes.OrderBy(c => order[c.Id])];
        explanation.UnexplainedChangeIds = prompt.Changes.Select(c => c.Id).Where(id => explanation.Changes.All(c => c.Id != id)).ToList();

        AddGroundingWarnings(explanation, prompt, result);
        return explanation;
    }

    private static void AddGroundingWarnings(ComparisonExplanationDto explanation, ExplanationPromptData prompt, ComparisonResultDto result)
    {
        var counts = new List<int> { result.TotalChanges, prompt.Changes.Count, prompt.Omitted };

        if (result.Stats is { } stats)
        {
            counts.AddRange([stats.Added, stats.Removed, stats.Replaced]);
        }

        var global = Evidence(prompt.Changes.SelectMany(c => c.Texts().Append(c.Page?.ToString() ?? string.Empty)), counts);

        if (explanation.Summary.Length > 0)
        {
            foreach (var claim in Unsupported(explanation.Summary, global))
            {
                explanation.GroundingWarnings.Add(new GroundingWarningDto { Message = claim });
            }

            if (result.TotalChanges > 0 && NoChangesRegex().IsMatch(explanation.Summary))
            {
                explanation.GroundingWarnings.Add(new GroundingWarningDto
                {
                    Message = "El resumen afirma que no hay cambios, pero la comparación determinista detectó cambios."
                });
            }
        }

        var inputs = prompt.Changes.ToDictionary(c => c.Id);

        foreach (var change in explanation.Changes)
        {
            var input = inputs[change.Id];
            var own = Evidence(input.Texts().Append(input.Page?.ToString() ?? string.Empty), counts);

            foreach (var claim in Unsupported(change.Explanation, own))
            {
                explanation.GroundingWarnings.Add(new GroundingWarningDto { ChangeId = change.Id, Message = claim });
            }

            if (NoChangesRegex().IsMatch(change.Explanation))
            {
                explanation.GroundingWarnings.Add(new GroundingWarningDto
                {
                    ChangeId = change.Id,
                    Message = "La explicación afirma que no hubo cambios, pero este cambio sí se detectó."
                });
            }
        }
    }

    private sealed record EvidenceSet(HashSet<string> Raw, HashSet<string> Digits, HashSet<string> Emails);

    private static EvidenceSet Evidence(IEnumerable<string> texts, IEnumerable<int> counts)
    {
        var raw = new HashSet<string>(StringComparer.Ordinal);
        var digits = new HashSet<string>(StringComparer.Ordinal);
        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var text in texts)
        {
            foreach (Match m in NumberRegex().Matches(text))
            {
                raw.Add(m.Value);
                digits.Add(DigitsOnly(m.Value));

                // Parte entera de un importe con decimales: «15.200,00» también se acepta como «15200».
                if (DecimalsRegex().Match(m.Value) is { Success: true } decimals)
                {
                    digits.Add(DigitsOnly(m.Value[..decimals.Index]));
                }

                // Cada parte de una fecha o cifra compuesta también cuenta ("15/11/2026" → 15, 11, 2026).
                foreach (var part in m.Value.Split('.', ',', ':', '/', '-'))
                {
                    digits.Add(part.TrimStart('0') is { Length: > 0 } p ? p : "0");
                }
            }

            foreach (Match m in EmailRegex().Matches(text))
            {
                emails.Add(m.Value.TrimEnd('.'));
            }
        }

        foreach (var n in counts)
        {
            raw.Add(n.ToString());
            digits.Add(n.ToString());
        }

        return new EvidenceSet(raw, digits, emails);
    }

    private static IEnumerable<string> Unsupported(string text, EvidenceSet evidence)
    {
        var reported = new HashSet<string>();

        foreach (Match m in NumberRegex().Matches(text))
        {
            var digits = DigitsOnly(m.Value);
            var trimmed = digits.TrimStart('0') is { Length: > 0 } t ? t : "0";

            if (!evidence.Raw.Contains(m.Value) && !evidence.Digits.Contains(digits) && !evidence.Digits.Contains(trimmed) && reported.Add(m.Value))
            {
                yield return $"La IA menciona «{m.Value}», que no aparece en los cambios detectados.";
            }
        }

        foreach (Match m in EmailRegex().Matches(text))
        {
            var email = m.Value.TrimEnd('.');

            if (!evidence.Emails.Contains(email) && reported.Add(email))
            {
                yield return $"La IA menciona el correo «{email}», que no aparece en los cambios detectados.";
            }
        }
    }

    private static string DigitsOnly(string value) => new(value.Where(char.IsDigit).ToArray());

    // Sin caracteres de control, sin los marcadores internos [[ ]] del mensaje y con longitud acotada.
    private static string Clean(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        text = text.Replace("[[", string.Empty, StringComparison.Ordinal).Replace("]]", string.Empty, StringComparison.Ordinal);

        var builder = new StringBuilder(Math.Min(text.Length, max));

        foreach (var c in text.Trim())
        {
            if (builder.Length >= max)
            {
                break;
            }

            if (!char.IsControl(c) || c == '\n')
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Trim();
    }
}
