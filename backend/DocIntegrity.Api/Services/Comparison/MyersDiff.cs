namespace DocIntegrity.Api.Services.Comparison;

public enum DiffKind
{
    Equal,
    Delete,
    Insert
}

// Tramo del guion de edición. Equal: A[AStart..AStart+Length) == B[BStart..BStart+Length).
// Delete: A[AStart..AStart+Length) no está en B (BStart = posición en B donde estaría).
// Insert: B[BStart..BStart+Length) no está en A (AStart = posición en A donde se inserta).
public readonly record struct DiffSegment(DiffKind Kind, int AStart, int BStart, int Length);

// Diferencia mínima entre dos secuencias (Myers, 1986: "An O(ND) Difference
// Algorithm and Its Variations"), con bisección por el "middle snake" para usar
// memoria lineal. Tiempo O((N+M)·D), memoria O(N+M).
//
// Es determinista: las mismas entradas producen siempre el mismo guion.
// Para limitar el tiempo ante documentos muy distintos, cada paso consume
// presupuesto; si se agota, Compute devuelve null.
public static class MyersDiff
{
    public static IReadOnlyList<DiffSegment>? Compute(int[] a, int[] b, long maxOperations)
    {
        var runner = new Runner(a, b, maxOperations);

        try
        {
            runner.Diff(0, a.Length, 0, b.Length);
        }
        catch (BudgetExceededException)
        {
            return null;
        }

        return Merge(runner.Segments);
    }

    // Une tramos contiguos del mismo tipo.
    private static List<DiffSegment> Merge(List<DiffSegment> segments)
    {
        var merged = new List<DiffSegment>(segments.Count);

        foreach (var segment in segments)
        {
            if (segment.Length == 0)
            {
                continue;
            }

            if (merged.Count > 0)
            {
                var last = merged[^1];

                if (last.Kind == segment.Kind &&
                    last.AStart + (segment.Kind == DiffKind.Insert ? 0 : last.Length) == segment.AStart &&
                    last.BStart + (segment.Kind == DiffKind.Delete ? 0 : last.Length) == segment.BStart)
                {
                    merged[^1] = last with { Length = last.Length + segment.Length };
                    continue;
                }
            }

            merged.Add(segment);
        }

        return merged;
    }

    private sealed class BudgetExceededException : Exception;

    private sealed class Runner
    {
        private readonly int[] _a;
        private readonly int[] _b;
        private long _budget;

        public List<DiffSegment> Segments { get; } = [];

        public Runner(int[] a, int[] b, long budget)
        {
            _a = a;
            _b = b;
            _budget = budget;
        }

        private void Spend()
        {
            if (--_budget < 0)
            {
                throw new BudgetExceededException();
            }
        }

        public void Diff(int aLo, int aHi, int bLo, int bHi)
        {
            // Prefijo común.
            var prefix = 0;

            while (aLo + prefix < aHi && bLo + prefix < bHi && _a[aLo + prefix] == _b[bLo + prefix])
            {
                prefix++;
            }

            if (prefix > 0)
            {
                Segments.Add(new DiffSegment(DiffKind.Equal, aLo, bLo, prefix));
                aLo += prefix;
                bLo += prefix;
            }

            // Sufijo común (se emite al final).
            var suffix = 0;

            while (aHi - suffix > aLo && bHi - suffix > bLo && _a[aHi - 1 - suffix] == _b[bHi - 1 - suffix])
            {
                suffix++;
            }

            aHi -= suffix;
            bHi -= suffix;

            if (aLo == aHi)
            {
                if (bLo < bHi)
                {
                    Segments.Add(new DiffSegment(DiffKind.Insert, aLo, bLo, bHi - bLo));
                }
            }
            else if (bLo == bHi)
            {
                Segments.Add(new DiffSegment(DiffKind.Delete, aLo, bLo, aHi - aLo));
            }
            else
            {
                var split = Bisect(aLo, aHi, bLo, bHi);

                if (split is { } point &&
                    !(point.X == aLo && point.Y == bLo) &&
                    !(point.X == aHi && point.Y == bHi))
                {
                    Diff(aLo, point.X, bLo, point.Y);
                    Diff(point.X, aHi, point.Y, bHi);
                }
                else
                {
                    // Sin elementos comunes: todo A se borra y todo B se inserta.
                    Segments.Add(new DiffSegment(DiffKind.Delete, aLo, bLo, aHi - aLo));
                    Segments.Add(new DiffSegment(DiffKind.Insert, aHi, bLo, bHi - bLo));
                }
            }

            if (suffix > 0)
            {
                Segments.Add(new DiffSegment(DiffKind.Equal, aHi, bHi, suffix));
            }
        }

        // Busca el punto medio de un camino de edición mínimo recorriendo a la
        // vez desde el inicio y desde el final hasta que ambos caminos se solapan.
        private (int X, int Y)? Bisect(int aLo, int aHi, int bLo, int bHi)
        {
            var n = aHi - aLo;
            var m = bHi - bLo;
            var maxD = (n + m + 1) / 2;
            var offset = maxD;
            var length = 2 * maxD + 2;

            var forward = new int[length];
            var reverse = new int[length];
            Array.Fill(forward, -1);
            Array.Fill(reverse, -1);
            forward[offset + 1] = 0;
            reverse[offset + 1] = 0;

            var delta = n - m;
            // Con delta impar el solape se detecta en el recorrido hacia delante.
            var front = (delta & 1) != 0;

            int kForwardStart = 0, kForwardEnd = 0, kReverseStart = 0, kReverseEnd = 0;

            for (var d = 0; d < maxD; d++)
            {
                for (var k = -d + kForwardStart; k <= d - kForwardEnd; k += 2)
                {
                    Spend();

                    var index = offset + k;
                    var x = k == -d || (k != d && forward[index - 1] < forward[index + 1])
                        ? forward[index + 1]
                        : forward[index - 1] + 1;
                    var y = x - k;

                    while (x < n && y < m && _a[aLo + x] == _b[bLo + y])
                    {
                        Spend();
                        x++;
                        y++;
                    }

                    forward[index] = x;

                    if (x > n)
                    {
                        kForwardEnd += 2;
                    }
                    else if (y > m)
                    {
                        kForwardStart += 2;
                    }
                    else if (front)
                    {
                        var reverseIndex = offset + delta - k;

                        if (reverseIndex >= 0 && reverseIndex < length && reverse[reverseIndex] != -1 &&
                            x >= n - reverse[reverseIndex])
                        {
                            return (aLo + x, bLo + y);
                        }
                    }
                }

                for (var k = -d + kReverseStart; k <= d - kReverseEnd; k += 2)
                {
                    Spend();

                    var index = offset + k;
                    var x = k == -d || (k != d && reverse[index - 1] < reverse[index + 1])
                        ? reverse[index + 1]
                        : reverse[index - 1] + 1;
                    var y = x - k;

                    while (x < n && y < m && _a[aLo + n - x - 1] == _b[bLo + m - y - 1])
                    {
                        Spend();
                        x++;
                        y++;
                    }

                    reverse[index] = x;

                    if (x > n)
                    {
                        kReverseEnd += 2;
                    }
                    else if (y > m)
                    {
                        kReverseStart += 2;
                    }
                    else if (!front)
                    {
                        var forwardIndex = offset + delta - k;

                        if (forwardIndex >= 0 && forwardIndex < length && forward[forwardIndex] != -1)
                        {
                            var forwardX = forward[forwardIndex];
                            var forwardY = offset + forwardX - forwardIndex;

                            if (forwardX >= n - x)
                            {
                                return (aLo + forwardX, bLo + forwardY);
                            }
                        }
                    }
                }
            }

            return null;
        }
    }
}
