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
public static class ChangeBuilder
{
    private const int MaxGapUnits = 2;
    private const int MaxGapWordLength = 3;

    private readonly record struct Hunk(int AStart, int AEnd, int BStart, int BEnd);

    public static (List<ChangeDto> Changes, ComparisonStatsDto Stats) Build(
        IReadOnlyList<TextUnit> original,
        IReadOnlyList<TextUnit> modified,
        IReadOnlyList<DiffSegment> segments,
        ChangeBuildContext context)
    {
        var hunks = BuildHunks(original, segments, context.MergeShortGaps);
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

            var originalSide = Side(original, hunk.AStart, hunk.AEnd, originalText, context.MaxTextLength);
            var modifiedSide = Side(modified, hunk.BStart, hunk.BEnd, modifiedText, context.MaxTextLength);

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

    private static List<Hunk> BuildHunks(IReadOnlyList<TextUnit> original, IReadOnlyList<DiffSegment> segments, bool mergeShortGaps)
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
                        mergeShortGaps &&
                        i + 1 < segments.Count &&
                        segment.Length <= MaxGapUnits &&
                        Enumerable.Range(segment.AStart, segment.Length).All(k => original[k].Text.Length <= MaxGapWordLength);

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

    private static ChangeSideDto Side(IReadOnlyList<TextUnit> units, int start, int end, string text, int maxLength)
    {
        if (end > start)
        {
            return new ChangeSideDto
            {
                Text = text.Length > maxLength ? text[..maxLength] : text,
                TextTruncated = text.Length > maxLength,
                Page = units[start].Page,
                Line = units[start].Line,
                EndPage = units[end - 1].Page,
                EndLine = units[end - 1].Line
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
