using System.Collections.Concurrent;
using DocIntegrity.Api.DTOs;
using Microsoft.Extensions.Options;

namespace DocIntegrity.Api.Services.Comparison;

// Comparación guardada en memoria para pedir después su explicación.
// Contiene solo el resultado determinista (cambios ya recortados), nunca los PDF ni su texto completo.
public sealed class CachedComparison
{
    public required Guid Id { get; init; }
    public required Guid OwnerUserId { get; init; }
    public required ComparisonResultDto Result { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    // Orden de inserción: decide cuál es la más antigua aunque coincidan las horas.
    public required long Sequence { get; init; }

    // Explicación ya generada (se reutiliza mientras la comparación siga en la caché).
    public ComparisonExplanationDto? Explanation { get; set; }

    // Evita generar dos veces la misma explicación en paralelo.
    public SemaphoreSlim Lock { get; } = new(1, 1);
}

// Caché en memoria, aislada por usuario y con vencimiento. Se pierde al reiniciar la API.
public sealed class ComparisonCache
{
    private readonly ConcurrentDictionary<Guid, CachedComparison> _entries = new();
    private readonly ExplanationOptions _options;
    private readonly TimeProvider _time;

    public ComparisonCache(IOptions<ExplanationOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;
    }

    public int Count => _entries.Count;

    public int CountFor(Guid ownerUserId) => _entries.Values.Count(e => e.OwnerUserId == ownerUserId);

    public CachedComparison Add(Guid ownerUserId, ComparisonResultDto result)
    {
        var now = _time.GetUtcNow();
        var entry = new CachedComparison
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            Result = result,
            CreatedAt = now,
            Sequence = Interlocked.Increment(ref _sequence),
            ExpiresAt = now.AddMinutes(_options.CacheMinutes)
        };

        // Las expulsiones y el alta van juntas para que los límites se respeten con solicitudes simultáneas.
        lock (_gate)
        {
            Purge();

            // 1. Límite por usuario: se descartan sus propias comparaciones más antiguas.
            EvictOldestWhile(e => e.OwnerUserId == ownerUserId, Math.Max(1, _options.MaxCachedComparisonsPerUser));

            // 2. Límite global: se descartan las más antiguas de todo el servidor.
            EvictOldestWhile(_ => true, Math.Max(1, _options.MaxCachedComparisons));

            _entries[entry.Id] = entry;
        }

        return entry;
    }

    private readonly object _gate = new();
    private long _sequence;

    private void EvictOldestWhile(Func<CachedComparison, bool> scope, int limit)
    {
        while (true)
        {
            var inScope = _entries.Values.Where(scope).ToList();

            if (inScope.Count < limit)
            {
                return;
            }

            var oldest = inScope.MinBy(e => e.Sequence)!;
            _entries.TryRemove(oldest.Id, out _);
        }
    }

    // null si no existe, venció o pertenece a otro usuario: el llamador responde lo mismo (404) en los tres casos.
    public CachedComparison? Get(Guid id, Guid ownerUserId)
    {
        if (!_entries.TryGetValue(id, out var entry))
        {
            return null;
        }

        if (entry.ExpiresAt <= _time.GetUtcNow())
        {
            _entries.TryRemove(id, out _);
            return null;
        }

        return entry.OwnerUserId == ownerUserId ? entry : null;
    }

    private void Purge()
    {
        var now = _time.GetUtcNow();

        foreach (var entry in _entries.Values.Where(e => e.ExpiresAt <= now))
        {
            _entries.TryRemove(entry.Id, out _);
        }
    }
}
