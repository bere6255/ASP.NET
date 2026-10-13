using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace PaymentsApi.HealthChecks;

public sealed class RedisHealthCheck(IConnectionMultiplexer connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await connection.GetDatabase().PingAsync();
        return HealthCheckResult.Healthy("Redis is reachable.");
    }
}
