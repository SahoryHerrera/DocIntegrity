using DocIntegrity.Api.DTOs;

namespace DocIntegrity.Api.Services.Comparison;

public sealed class ChangeBuildContext
{
    public required IReadOnlyList<SectionHeading> HeadingsOriginal { get; init; }
    public required IReadOnlyList<SectionHeading> HeadingsModified { get; init; }
    public required IReadOnlySet<int> UnreliablePagesOriginal { get; init; }
    public required IReadOnlySet<int> UnreliablePagesModified { get; init; }
    // Une con espacio (palabras) o salto de línea (líneas).
    public required string Separator { get; init; }
    // Fusiona cambios separados por 1-2 palabras cortas iguales ("de", "la").
    public required bool MergeShortGaps { get; init; }
    public required int ContextUnits { get; init; }
    public required int MaxTextLength { get; init; }
}

// Convierte el guion de edición de Myers en cambios legibles:
// solo inserciones → added; solo borrados → removed; ambos → replaced.
public static partial class ChangeBuilder
{
    private const int MaxGapUnits = 2;
    private const int MaxGapWordLength = 3;

    // Tramo de cambio: A[AStart..AEnd) en el original y B[BStart..BEnd) en la nueva versión.
    public readonly record struct Hunk(int AStart, int AEnd, int BStart, int BEnd);

    // Marcadores de estructura: «3.», «2.1.», «IV.», «a)», viñetas. No son palabras de una frase.
    [System.Text.RegularExpressions.GeneratedRegex(@"^(\d{1,3}(\.\d{1,3})*[.)]|[IVXLCivxlc]{1,6}[.)]|[A-Za-z][.)]|[-•–·*])$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex StructuralMarkerRegex();

    public static (List<ChangeDto> Changes, ComparisonStatsDto Stats) Build(
        IReadOnlyList<TextUnit> original,
        IReadOnlyList<TextUnit> modified,
        IReadOnlyList<DiffSegment> segments,
        ChangeBuildContext context)
    {
        var hunks = BuildHunks(original, modified, segments, context);
        var changes = new List<ChangeDto>(hunks.Count);
        var pagesOriginal = new SortedSet<int>();
        var pagesModified = new SortedSet<int>();

        for (var i = 0; i < hunks.Count; i++)
        {
            var hunk = hunks[i];
            var previousEnd = i > 0 ? hunks[i - 1].BEnd : 0;
            var nextStart = i + 1 < hunks.Count ? hunks[i + 1].BStart : modified.Count;

            var originalText = Join(original, hunk.AStart, hunk.AEnd, context.Separator);
            var modifiedText = Join(modified, hunk.BStart, hunk.BEnd, context.Separator);
            var before = Join(modified, Math.Max(previousEnd, hunk.BStart - context.ContextUnits), hunk.BStart, context.Separator);
            var after = Join(modified, hunk.BEnd, Math.Min(nextStart, hunk.BEnd + context.ContextUnits), context.Separator);

            var type = (hunk.AEnd > hunk.AStart, hunk.BEnd > hunk.BStart) switch
            {
                (true, true) => "replaced",
                (true, false) => "removed",
                _ => "added"
            };

            var originalSide = Side(original, hunk.AStart, hunk.AEnd, originalText, context.MaxTextLength, context.Separator);
            var modifiedSide = Side(modified, hunk.BStart, hunk.BEnd, modifiedText, context.MaxTextLength, context.Separator);

            var touchedOriginal = Pages(original, hunk.AStart, hunk.AEnd, originalSide);
            var touchedModified = Pages(modified, hunk.BStart, hunk.BEnd, modifiedSide);

            if (hunk.AEnd > hunk.AStart)
            {
                pagesOriginal.UnionWith(touchedOriginal);
            }

            if (hunk.BEnd > hunk.BStart)
            {
                pagesModified.UnionWith(touchedModified);
            }

            // La sección se busca en el lado que contiene el texto del cambio.
            var heading = type == "removed"
                ? FindSection(context.HeadingsOriginal, originalSide)
                : FindSection(context.HeadingsModified, modifiedSide);

            var tags = ChangeTagger.Tag(originalText, modifiedText, before, after);

            changes.Add(new ChangeDto
            {
                Id = $"c{i + 1}",
                Type = type,
                Original = originalSide,
                Modified = modifiedSide,
                Context = new ChangeContextDto { Before = before, After = after },
                Section = heading is null ? null : new ChangeSectionDto { Title = heading.Title, Page = heading.Page },
                Tags = tags,
                Suspicious = tags.Any(ChangeTagger.SuspiciousTags.Contains),
                OnIncompletePage =
                    touchedOriginal.Overlaps(context.UnreliablePagesOriginal) ||
                    touchedModified.Overlaps(context.UnreliablePagesModified)
            });
        }

        int unchanged = 0, removedUnits = 0, addedUnits = 0;

        foreach (var segment in segments)
        {
            switch (segment.Kind)
            {
                case DiffKind.Equal: unchanged += segment.Length; break;
                case DiffKind.Delete: removedUnits += segment.Length; break;
                case DiffKind.Insert: addedUnits += segment.Length; break;
            }
        }

        var total = original.Count + modified.Count;

        var stats = new ComparisonStatsDto
        {
            Added = changes.Count(c => c.Type == "added"),
            Removed = changes.Count(c => c.Type == "removed"),
            Replaced = changes.Count(c => c.Type == "replaced"),
            UnitsAdded = addedUnits,
            UnitsRemoved = removedUnits,
            UnitsUnchanged = unchanged,
            Similarity = total == 0 ? 1 : Math.Round(2.0 * unchanged / total, 4),
            PagesAffectedOriginal = [.. pagesOriginal],
            PagesAffectedModified = [.. pagesModified]
        };

        return (changes, stats);
    }

    // Agrupa los tramos no iguales del guion de Myers en cambios.
    // Un hueco corto (1-2 palabras iguales de hasta 3 letras: «de», «la», «por») entre dos tramos se fusiona
    // para no fragmentar una misma frase, SALVO que separe modificaciones independientes:
    //   1. el hueco es un marcador de estructura («3.», «IV.», «a)», viñetas);
    //   2. los dos tramos están en secciones distintas (en cualquiera de los dos documentos);
    //   3. el hueco cruza un salto de línea y el primer tramo termina una frase (. ; : ! ?).
    // Un reemplazo de una frase que ocupa varias líneas sigue siendo un solo cambio.
    // Pública para las pruebas: los tramos y los huecos iguales cubren cada unidad exactamente una vez.
    public static List<Hunk> BuildHunks(
        IReadOnlyList<TextUnit> original,
        IReadOnlyList<TextUnit> modified,
        IReadOnlyList<DiffSegment> segments,
        ChangeBuildContext context)
    {
        var hunks = new List<Hunk>();
        Hunk? open = null;

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];

            switch (segment.Kind)
            {
                case DiffKind.Delete:
                    open = open is { } d
                        ? d with { AEnd = segment.AStart + segment.Length }
                        : new Hunk(segment.AStart, segment.AStart + segment.Length, segment.BStart, segment.BStart);
                    break;

                case DiffKind.Insert:
                    open = open is { } h
                        ? h with { BEnd = segment.BStart + segment.Length }
                        : new Hunk(segment.AStart, segment.AStart, segment.BStart, segment.BStart + segment.Length);
                    break;

                case DiffKind.Equal when open is { } current:
                    var isShortGap =
                        context.MergeShortGaps &&
                        i + 1 < segments.Count &&
                        segment.Length <= MaxGapUnits &&
                        Enumerable.Range(segment.AStart, segment.Length).All(k => original[k].Text.Length <= MaxGapWordLength) &&
                        !SeparatesIndependentChanges(current, segment, original, modified, context);

                    if (isShortGap)
                    {
                        open = current with { AEnd = current.AEnd + segment.Length, BEnd = current.BEnd + segment.Length };
                    }
                    else
                    {
                        hunks.Add(current);
                        open = null;
                    }

                    break;
            }
        }

        if (open is { } last)
        {
            hunks.Add(last);
        }

        return hunks;
    }

    private static bool SeparatesIndependentChanges(
        Hunk previous,
        DiffSegment gap,
        IReadOnlyList<TextUnit> original,
        IReadOnlyList<TextUnit> modified,
        ChangeBuildContext context)
    {
        // 1. Marcador de estructura en el hueco.
        if (Enumerable.Range(gap.AStart, gap.Length).Any(k => StructuralMarkerRegex().IsMatch(original[k].Text)))
        {
            return true;
        }

        // Posiciones a cada lado del hueco: la unidad justo antes (último cambio del tramo anterior o
        // su punto de inserción) y la primera después (inicio del tramo siguiente).
        var sides = new[]
        {
            (Units: original, Before: gap.AStart - 1, After: gap.AStart + gap.Length, HasChange: previous.AEnd > previous.AStart, Headings: context.HeadingsOriginal),
            (Units: modified, Before: gap.BStart - 1, After: gap.BStart + gap.Length, HasChange: previous.BEnd > previous.BStart, Headings: context.HeadingsModified)
        };

        var crossesLine = false;
        var endsSentence = false;

        foreach (var side in sides)
        {
            if (side.Units.Count == 0)
            {
                continue;
            }

            var before = side.Units[Math.Clamp(side.Before, 0, side.Units.Count - 1)];
            var after = side.Units[Math.Clamp(side.After, 0, side.Units.Count - 1)];

            // 2. Secciones distintas.
            var sectionBefore = SectionDetector.Find(side.Headings, before.Page, before.Line);
            var sectionAfter = SectionDetector.Find(side.Headings, after.Page, after.Line);

            if (sectionBefore != sectionAfter)
            {
                return true;
            }

            crossesLine |= before.Page != after.Page || before.Line != after.Line;

            if (side.HasChange && side.Before >= 0)
            {
                endsSentence |= EndsSentence(side.Units[side.Before].Text);
            }
        }

        // 3. Salto de línea después de una frase terminada.
        return crossesLine && endsSentence;
    }

    // «contrato.», «pago;», «(anexo).», «"fin."» → true.
    private static bool EndsSentence(string word)
    {
        var trimmed = word.TrimEnd(')', ']', '"', '\'', '\u00BB', '\u201D');
        return trimmed.Length > 0 && trimmed[^1] is '.' or ';' or ':' or '!' or '?';
    }

    private static ChangeSideDto Side(IReadOnlyList<TextUnit> units, int start, int end, string text, int maxLength, string separator)
    {
        if (end > start)
        {
            // Resto de la misma línea: desde el inicio de la línea del primer cambio
            // y hasta el final de la línea del último, sin pasar a líneas vecinas.
            var first = units[start];
            var last = units[end - 1];
            var lineStart = start;
            var lineEnd = end;

            while (lineStart > 0 && units[lineStart - 1].Page == first.Page && units[lineStart - 1].Line == first.Line)
            {
                lineStart--;
            }

            while (lineEnd < units.Count && units[lineEnd].Page == last.Page && units[lineEnd].Line == last.Line)
            {
                lineEnd++;
            }

            var before = Join(units, lineStart, start, separator);
            var after = Join(units, end, lineEnd, separator);

            return new ChangeSideDto
            {
                Text = text.Length > maxLength ? text[..maxLength] : text,
                TextTruncated = text.Length > maxLength,
                Page = first.Page,
                Line = first.Line,
                EndPage = last.Page,
                EndLine = last.Line,
                LineBefore = before.Length > maxLength ? before[^maxLength..] : before,
                LineAfter = after.Length > maxLength ? after[..maxLength] : after
            };
        }

        // Sin texto en este lado: posición del cambio (después de la unidad anterior,
        // o antes de la siguiente si está al principio).
        var anchor = start > 0 ? units[start - 1] : start < units.Count ? units[start] : null;

        return new ChangeSideDto
        {
            InsertionPoint = true,
            Page = anchor?.Page,
            Line = anchor?.Line,
            EndPage = anchor?.Page,
            EndLine = anchor?.Line
        };
    }

    private static HashSet<int> Pages(IReadOnlyList<TextUnit> units, int start, int end, ChangeSideDto side)
    {
        var pages = new HashSet<int>();

        for (var k = start; k < end; k++)
        {
            pages.Add(units[k].Page);
        }

        if (pages.Count == 0 && side.Page is { } anchorPage)
        {
            pages.Add(anchorPage);
        }

        return pages;
    }

    private static SectionHeading? FindSection(IReadOnlyList<SectionHeading> headings, ChangeSideDto side) =>
        side.Page is { } page && side.Line is { } line ? SectionDetector.Find(headings, page, line) : null;

    private static string Join(IReadOnlyList<TextUnit> units, int start, int end, string separator)
    {
        if (end <= start)
        {
            return string.Empty;
        }

        return string.Join(separator, Enumerable.Range(start, end - start).Select(k => units[k].Text));
    }
}
