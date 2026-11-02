using DocIntegrity.Api.DTOs;
using DocIntegrity.Api.Services.Comparison;

namespace DocIntegrity.Api.Tests;

// Agrupación de cambios: los huecos cortos solo se fusionan dentro de una misma frase,
// nunca entre cláusulas o campos independientes.
public class ChangeGroupingTests
{
    private static Task<ComparisonResultDto> Compare(string[] original, string[] modified) =>
        ExplanationSamples.CompareAsync(original, modified);

    [Fact]
    public async Task Regression_SurchargeAndConfidentialityToExclusivity_AreTwoChanges()
    {
        string[] Contract(string penalty, string clauseTitle, string clauseText) =>
        [
            "CONTRATO DE PRESTACION DE SERVICIOS (FICTICIO)",
            "1. Objeto",
            "El proveedor prestara servicios de mantenimiento.",
            "2. Penalizacion",
            $"El retraso en el pago genera un recargo del {penalty}",
            clauseTitle,
            clauseText,
            "4. Vigencia",
            "Doce meses."
        ];

        var result = await Compare(
            Contract("2 por ciento.", "3. Confidencialidad", "Las partes mantendran la confidencialidad de la informacion."),
            Contract("10 por ciento y la rescision inmediata del contrato.", "3. Exclusividad", "El cliente no podra contratar a otros proveedores durante la vigencia."));

        Assert.Equal(2, result.Changes.Count);
        var (surcharge, clause) = (result.Changes[0], result.Changes[1]);

        // El recargo sigue siendo un solo cambio: «2 … ciento.» se une por «por», en la misma línea.
        Assert.Equal("2 por ciento.", surcharge.Original.Text);
        Assert.Equal("10 por ciento y la rescision inmediata del contrato.", surcharge.Modified.Text);
        Assert.Equal("2. Penalizacion", surcharge.Section?.Title);

        // La cláusula 3 es otro cambio: «3.» es un marcador de estructura y empieza otra sección.
        Assert.Equal("Confidencialidad Las partes mantendran la confidencialidad de la informacion.", clause.Original.Text);
        Assert.Equal("Exclusividad El cliente no podra contratar a otros proveedores durante la vigencia.", clause.Modified.Text);
        Assert.Equal("3. Exclusividad", clause.Section?.Title);
        Assert.Equal((0, 0, 2), (result.Stats!.Added, result.Stats.Removed, result.Stats.Replaced));
    }

    [Fact]
    public async Task IndependentChangesOnConsecutiveLines_AreNotMerged()
    {
        // Antes: «Un» (2 letras) unía los dos cambios aunque el primero terminaba la frase en otra línea.
        var result = await Compare(
            ["El pago se hara en efectivo.", "Un recargo aplica al retraso."],
            ["El pago se hara por transferencia.", "Un descuento aplica al retraso."]);

        Assert.Equal(2, result.Changes.Count);
        Assert.Equal(("en efectivo.", "por transferencia."), (result.Changes[0].Original.Text, result.Changes[0].Modified.Text));
        Assert.Equal(("recargo", "descuento"), (result.Changes[1].Original.Text, result.Changes[1].Modified.Text));
        Assert.Equal((1, 2), (result.Changes[0].Modified.Line!.Value, result.Changes[1].Modified.Line!.Value));
    }

    [Fact]
    public async Task ShortGapWithinTheSameSentenceAndLine_IsStillMerged()
    {
        var result = await Compare(["Se aplica un recargo del 2 por ciento."], ["Se aplica un recargo del 5 por mil."]);

        var change = Assert.Single(result.Changes);
        Assert.Equal(("2 por ciento.", "5 por mil."), (change.Original.Text, change.Modified.Text));
    }

    [Fact]
    public async Task MultilineParagraphReplacement_StaysOneChange()
    {
        // Una sola frase repartida en tres líneas; las palabras cortas que coinciden («de», «la») no la parten.
        var result = await Compare(
            ["El arrendatario se obliga a pagar la renta", "de manera puntual durante los primeros", "cinco dias naturales del mes."],
            ["El inquilino debera abonar la cuota", "de forma anticipada antes del", "vencimiento acordado por escrito."]);

        var change = Assert.Single(result.Changes);
        Assert.Equal("replaced", change.Type);
        Assert.Equal((1, 3), (change.Modified.Line!.Value, change.Modified.EndLine!.Value));
        Assert.Equal((1, 3), (change.Original.Line!.Value, change.Original.EndLine!.Value));
    }

    [Fact]
    public async Task OriginalTestContract_KeepsItsThreeReplacements()
    {
        string[] Lease(string amount, string due, string month) =>
        [
            "DOCUMENTO FICTICIO PARA PRUEBAS - HUELLA",
            "CONTRATO DE ARRENDAMIENTO DE OFICINA",
            "2. Renta",
            $"El arrendatario pagara una renta mensual de {amount} pesos.",
            $"El primer pago vence el {due}.",
            $"Fecha de inicio: 1 de {month} de 2026.",
            "3. Deposito",
            "Se entrega un deposito equivalente a un mes de renta."
        ];

        var result = await Compare(Lease("12.500,00", "15/11/2026", "noviembre"), Lease("15.200,00", "30/11/2026", "diciembre"));

        Assert.Equal(
            [("12.500,00", "15.200,00"), ("15/11/2026.", "30/11/2026."), ("noviembre", "diciembre")],
            result.Changes.Select(c => (c.Original.Text, c.Modified.Text)));
        Assert.All(result.Changes, c => Assert.Equal("replaced", c.Type));
    }

    [Fact]
    public async Task IdsStatsPagesAndAiReferences_AreConsistent()
    {
        var result = await Compare(
            ["1. Objeto", "Servicio de limpieza.", "2. Precio", "Importe de 100 pesos.", "3. Plazo", "Doce meses.", "4. Firma", "Las partes firman."],
            ["1. Objeto", "Servicio de limpieza y jardineria.", "2. Precio", "Importe de 300 pesos.", "Pago por adelantado.", "3. Plazo", "4. Firma", "Las partes firman."]);

        Assert.Equal(Enumerable.Range(1, result.Changes.Count).Select(i => $"c{i}"), result.Changes.Select(c => c.Id));
        Assert.Equal(result.TotalChanges, result.Changes.Count);
        Assert.Equal(result.Changes.Count(c => c.Type == "added"), result.Stats!.Added);
        Assert.Equal(result.Changes.Count(c => c.Type == "removed"), result.Stats.Removed);
        Assert.Equal(result.Changes.Count(c => c.Type == "replaced"), result.Stats.Replaced);

        var modifiedPages = result.Changes.Where(c => !c.Modified.InsertionPoint).Select(c => c.Modified.Page!.Value).Distinct().Order();
        var originalPages = result.Changes.Where(c => !c.Original.InsertionPoint).Select(c => c.Original.Page!.Value).Distinct().Order();
        Assert.Equal(modifiedPages, result.Stats.PagesAffectedModified);
        Assert.Equal(originalPages, result.Stats.PagesAffectedOriginal);

        // Las referencias de la IA son exactamente los ids de los cambios, y una respuesta completa no pierde ninguno.
        var prompt = ExplanationPrompt.Build(result, new ExplanationOptions());
        Assert.Equal(result.Changes.Select(c => c.Id), prompt.Changes.Select(c => c.Id));
        var explanation = ExplanationValidator.Validate(ExplanationSamples.AiJson(result), prompt, result, new ExplanationOptions());
        Assert.Equal(result.Changes.Select(c => c.Id), explanation.Changes.Select(c => c.Id));
        Assert.Empty(explanation.UnexplainedChangeIds);
        Assert.Empty(explanation.DiscardedChangeIds);
    }

    // Al separar cambios, cada unidad de los dos documentos queda en exactamente un tramo o en un hueco igual.
    [Fact]
    public void Grouping_NeverLosesNorDuplicatesUnits()
    {
        var random = new Random(4242);
        string[] vocabulary = ["de", "la", "el", "por", "un", "y", "3.", "IV.", "a)", "-", "pago", "renta", "fecha.", "clausula", "contrato;", "plazo", "importe", "fin.", "parte", "datos"];

        for (var round = 0; round < 1500; round++)
        {
            var original = RandomLines(random, vocabulary);
            var modified = Mutate(random, original, vocabulary);
            var (unitsA, unitsB, segments, context) = Prepare(original, modified);

            var hunks = ChangeBuilder.BuildHunks(unitsA, unitsB, segments, context);
            var unmerged = ChangeBuilder.BuildHunks(unitsA, unitsB, segments, WithoutMerging(context));

            AssertCoverage(unitsA, unitsB, segments, hunks);
            AssertCoverage(unitsA, unitsB, segments, unmerged);
            Assert.True(hunks.Count <= unmerged.Count, "fusionar nunca crea más cambios");
        }
    }

    private static void AssertCoverage(IReadOnlyList<TextUnit> a, IReadOnlyList<TextUnit> b, IReadOnlyList<DiffSegment> segments, List<ChangeBuilder.Hunk> hunks)
    {
        var inHunkA = new int[a.Count];
        var inHunkB = new int[b.Count];
        int lastA = 0, lastB = 0;

        foreach (var h in hunks)
        {
            Assert.True(h.AStart >= lastA && h.BStart >= lastB && h.AEnd >= h.AStart && h.BEnd >= h.BStart, "tramos ordenados y sin solaparse");
            Assert.True(h.AEnd > h.AStart || h.BEnd > h.BStart, "sin tramos vacíos");

            for (var k = h.AStart; k < h.AEnd; k++) inHunkA[k]++;
            for (var k = h.BStart; k < h.BEnd; k++) inHunkB[k]++;

            (lastA, lastB) = (h.AEnd, h.BEnd);
        }

        Assert.All(inHunkA, n => Assert.True(n <= 1, "unidad del original duplicada"));
        Assert.All(inHunkB, n => Assert.True(n <= 1, "unidad de la nueva versión duplicada"));

        // Toda unidad borrada o insertada está dentro de un tramo.
        foreach (var s in segments)
        {
            if (s.Kind == DiffKind.Delete) for (var k = s.AStart; k < s.AStart + s.Length; k++) Assert.Equal(1, inHunkA[k]);
            if (s.Kind == DiffKind.Insert) for (var k = s.BStart; k < s.BStart + s.Length; k++) Assert.Equal(1, inHunkB[k]);
        }

        // Lo que queda fuera de los tramos es idéntico en ambos documentos y aparece en el mismo orden.
        var outsideA = Enumerable.Range(0, a.Count).Where(k => inHunkA[k] == 0).Select(k => a[k].Text).ToList();
        var outsideB = Enumerable.Range(0, b.Count).Where(k => inHunkB[k] == 0).Select(k => b[k].Text).ToList();
        Assert.Equal(outsideA, outsideB);
    }

    private static (List<TextUnit>, List<TextUnit>, IReadOnlyList<DiffSegment>, ChangeBuildContext) Prepare(string[] original, string[] modified)
    {
        var linesA = original.Select((t, i) => new ExtractedLine(1 + i / 6, 1 + i % 6, t)).ToList();
        var linesB = modified.Select((t, i) => new ExtractedLine(1 + i / 6, 1 + i % 6, t)).ToList();
        var unitsA = TextNormalizer.ToWords(linesA);
        var unitsB = TextNormalizer.ToWords(linesB);
        var ids = new Dictionary<string, int>();
        int Id(TextUnit u) => ids.TryGetValue(u.Text, out var v) ? v : ids[u.Text] = ids.Count;
        var segments = MyersDiff.Compute(unitsA.Select(Id).ToArray(), unitsB.Select(Id).ToArray(), long.MaxValue)!;

        var context = new ChangeBuildContext
        {
            HeadingsOriginal = SectionDetector.Detect(linesA),
            HeadingsModified = SectionDetector.Detect(linesB),
            UnreliablePagesOriginal = new HashSet<int>(),
            UnreliablePagesModified = new HashSet<int>(),
            Separator = " ",
            MergeShortGaps = true,
            ContextUnits = 8,
            MaxTextLength = 10_000
        };

        return (unitsA, unitsB, segments, context);
    }

    private static ChangeBuildContext WithoutMerging(ChangeBuildContext c) => new()
    {
        HeadingsOriginal = c.HeadingsOriginal,
        HeadingsModified = c.HeadingsModified,
        UnreliablePagesOriginal = c.UnreliablePagesOriginal,
        UnreliablePagesModified = c.UnreliablePagesModified,
        Separator = c.Separator,
        MergeShortGaps = false,
        ContextUnits = c.ContextUnits,
        MaxTextLength = c.MaxTextLength
    };

    private static string[] RandomLines(Random random, string[] vocabulary) =>
        Enumerable.Range(0, random.Next(1, 9))
            .Select(_ => string.Join(' ', Enumerable.Range(0, random.Next(1, 7)).Select(_ => vocabulary[random.Next(vocabulary.Length)])))
            .ToArray();

    private static string[] Mutate(Random random, string[] lines, string[] vocabulary) =>
        lines.Select(line => random.Next(3) == 0
                ? string.Join(' ', line.Split(' ').Select(w => random.Next(3) == 0 ? vocabulary[random.Next(vocabulary.Length)] : w))
                : line)
            .Concat(random.Next(4) == 0 ? [vocabulary[random.Next(vocabulary.Length)]] : [])
            .ToArray();
}
