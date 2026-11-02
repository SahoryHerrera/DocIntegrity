using DocIntegrity.Api.Services.Comparison;

namespace DocIntegrity.Api.Tests;

// El guion de Myers se contrasta con una implementación de referencia: la
// longitud de la subsecuencia común más larga (LCS) por programación dinámica.
// Un guion es correcto si reconstruye B a partir de A, y es mínimo si su número
// de ediciones es n + m − 2·LCS.
public class MyersDiffTests
{
    private const long Unlimited = long.MaxValue;

    [Theory]
    [InlineData(new int[0], new int[0])]
    [InlineData(new int[0], new[] { 1, 2, 3 })]
    [InlineData(new[] { 1, 2, 3 }, new int[0])]
    [InlineData(new[] { 1, 2, 3 }, new[] { 1, 2, 3 })]
    [InlineData(new[] { 1 }, new[] { 2 })]
    [InlineData(new[] { 1, 2, 3 }, new[] { 4, 5, 6 })]
    [InlineData(new[] { 1, 2, 3, 4 }, new[] { 1, 9, 3, 4 })]
    [InlineData(new[] { 1, 1, 1, 1 }, new[] { 1, 1 })]
    [InlineData(new[] { 1, 2, 1, 2, 1 }, new[] { 2, 1, 2, 1, 2 })]
    [InlineData(new[] { 7, 1, 2, 3 }, new[] { 1, 2, 3, 7 })]
    public void EdgeCases_AreValidAndMinimal(int[] a, int[] b) => AssertValidAndMinimal(a, b);

    [Fact]
    public void RandomSmallSequences_MatchReference()
    {
        var random = new Random(20261101);

        for (var i = 0; i < 20_000; i++)
        {
            var alphabet = random.Next(1, 6);
            var a = RandomSequence(random, random.Next(0, 30), alphabet);
            var b = RandomSequence(random, random.Next(0, 30), alphabet);

            AssertValidAndMinimal(a, b);
        }
    }

    [Fact]
    public void RandomLargerSequences_MatchReference()
    {
        var random = new Random(31337);

        for (var i = 0; i < 300; i++)
        {
            var a = RandomSequence(random, random.Next(0, 300), random.Next(2, 12));
            var b = random.Next(2) == 0
                ? Mutate(random, a, random.Next(0, 20))
                : RandomSequence(random, random.Next(0, 300), random.Next(2, 12));

            AssertValidAndMinimal(a, b);
        }
    }

    [Fact]
    public void IsDeterministic()
    {
        var random = new Random(5);
        var a = RandomSequence(random, 500, 8);
        var b = Mutate(random, a, 40);

        var first = MyersDiff.Compute(a, b, Unlimited);
        var second = MyersDiff.Compute(a, b, Unlimited);

        Assert.Equal(first, second);
    }

    [Fact]
    public void IdenticalSequences_CostNothing()
    {
        var a = Enumerable.Range(0, 100_000).ToArray();

        var segments = MyersDiff.Compute(a, (int[])a.Clone(), maxOperations: 0);

        Assert.NotNull(segments);
        Assert.Equal([new DiffSegment(DiffKind.Equal, 0, 0, 100_000)], segments);
    }

    [Fact]
    public void ExceededBudget_ReturnsNull()
    {
        // Sin nada en común: D = 4000; un presupuesto de 1000 pasos no alcanza.
        var a = Enumerable.Range(0, 2000).ToArray();
        var b = Enumerable.Range(5000, 2000).ToArray();

        Assert.Null(MyersDiff.Compute(a, b, maxOperations: 1_000));
        Assert.NotNull(MyersDiff.Compute(a, b, Unlimited));
    }

    private static void AssertValidAndMinimal(int[] a, int[] b)
    {
        var segments = MyersDiff.Compute(a, b, Unlimited);
        Assert.NotNull(segments);

        int ai = 0, bi = 0, edits = 0;
        DiffKind? previous = null;

        foreach (var segment in segments)
        {
            Assert.True(segment.Length > 0, "tramo vacío");
            Assert.NotEqual(previous, segment.Kind);
            Assert.Equal(ai, segment.AStart);
            Assert.Equal(bi, segment.BStart);

            switch (segment.Kind)
            {
                case DiffKind.Equal:
                    for (var k = 0; k < segment.Length; k++)
                    {
                        Assert.Equal(a[ai + k], b[bi + k]);
                    }

                    ai += segment.Length;
                    bi += segment.Length;
                    break;
                case DiffKind.Delete:
                    ai += segment.Length;
                    edits += segment.Length;
                    break;
                case DiffKind.Insert:
                    bi += segment.Length;
                    edits += segment.Length;
                    break;
            }

            previous = segment.Kind;
        }

        Assert.Equal(a.Length, ai);
        Assert.Equal(b.Length, bi);
        Assert.Equal(a.Length + b.Length - 2 * Lcs(a, b), edits);
    }

    // Referencia: LCS por programación dinámica, O(n·m).
    private static int Lcs(int[] a, int[] b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                current[j] = a[i - 1] == b[j - 1] ? previous[j - 1] + 1 : Math.Max(previous[j], current[j - 1]);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private static int[] RandomSequence(Random random, int length, int alphabet) =>
        Enumerable.Range(0, length).Select(_ => random.Next(alphabet)).ToArray();

    // Copia con algunas inserciones, borrados y sustituciones.
    private static int[] Mutate(Random random, int[] source, int mutations)
    {
        var list = source.ToList();

        for (var i = 0; i < mutations; i++)
        {
            var position = list.Count == 0 ? 0 : random.Next(list.Count);

            switch (random.Next(3))
            {
                case 0: list.Insert(position, random.Next(100)); break;
                case 1 when list.Count > 0: list.RemoveAt(position); break;
                case 2 when list.Count > 0: list[position] = random.Next(100); break;
            }
        }

        return list.ToArray();
    }
}
