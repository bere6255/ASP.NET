using System.Collections.Concurrent;

namespace PaymentsApi.Services;

public sealed class InMemoryIdempotencyLock : IIdempotencyLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new(StringComparer.Ordinal);

    public Task<IAsyncDisposable?> TryAcquireAsync(
        string key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var semaphore = locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        IAsyncDisposable? lease = semaphore.Wait(0) ? new InMemoryLockLease(semaphore) : null;
        return Task.FromResult(lease);
    }

    private sealed class InMemoryLockLease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private SemaphoreSlim? heldSemaphore = semaphore;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref heldSemaphore, null)?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
