using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PaymentsApi.Config;

namespace PaymentsApi.HealthChecks;

public sealed class RabbitMqHealthCheck(IOptions<RabbitMqOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(options.Value.HostName, 5672, cancellationToken);
            return HealthCheckResult.Healthy("RabbitMQ is reachable.");
        }
        catch (SocketException)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ is unreachable.");
        }
    }
}
