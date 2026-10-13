using StackExchange.Redis;

namespace PaymentsApi.Services;

public sealed class RedisIdempotencyLock(IConnectionMultiplexer connection) : IIdempotencyLock
{
    private static readonly TimeSpan LockLifetime = TimeSpan.FromSeconds(10);
    private static readonly LuaScript ReleaseScript = LuaScript.Prepare(
        "if redis.call('get', @key) == @token then return redis.call('del', @key) else return 0 end");

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        string key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var token = Guid.NewGuid().ToString("N");
        var database = connection.GetDatabase();
        var acquired = await database.StringSetAsync(key, token, LockLifetime, When.NotExists);
        return acquired ? new RedisLockLease(database, key, token) : null;
    }

    private sealed class RedisLockLease(IDatabase database, string key, string token) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await database.ScriptEvaluateAsync(ReleaseScript, new { key, token });
        }
    }
}
