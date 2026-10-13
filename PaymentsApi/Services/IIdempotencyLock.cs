namespace PaymentsApi.Services;

public interface IIdempotencyLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(string key, CancellationToken cancellationToken);
}
