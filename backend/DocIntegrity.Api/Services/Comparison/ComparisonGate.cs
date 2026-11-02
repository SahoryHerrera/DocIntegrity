using Microsoft.Extensions.Options;

namespace DocIntegrity.Api.Services.Comparison;

// Limita las comparaciones simultáneas de este servidor (memoria y CPU acotadas).
// Se toma el turno ANTES de leer los archivos de la solicitud.
public sealed class ComparisonGate
{
    private readonly SemaphoreSlim _semaphore;

    public ComparisonGate(IOptions<ComparisonOptions> options)
    {
        var slots = Math.Max(1, options.Value.MaxConcurrentComparisons);
        _semaphore = new SemaphoreSlim(slots, slots);
    }

    public int AvailableSlots => _semaphore.CurrentCount;

    // null si no hay turno dentro del tiempo de espera.
    public async Task<Lease?> TryEnterAsync(TimeSpan wait, CancellationToken cancellationToken) =>
        await _semaphore.WaitAsync(wait, cancellationToken) ? new Lease(_semaphore) : null;

    public sealed class Lease : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private Task? _pendingWork;
        private int _released;

        internal Lease(SemaphoreSlim semaphore) => _semaphore = semaphore;

        // Si se respondió por tiempo agotado pero la extracción sigue en curso,
        // el turno no se libera hasta que termine: la CPU sigue acotada.
        public void HoldUntil(Task work) => _pendingWork = work;

        public void Dispose()
        {
            if (_pendingWork is { IsCompleted: false } work)
            {
                work.ContinueWith(_ => Release(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }
            else
            {
                Release();
            }
        }

        private void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _semaphore.Release();
            }
        }
    }
}
