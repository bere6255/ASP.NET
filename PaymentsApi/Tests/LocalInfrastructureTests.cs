using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaymentsApi.Extensions;
using PaymentsApi.Messaging;
using PaymentsApi.Models.Entities;
using PaymentsApi.Services;
using StackExchange.Redis;
using Xunit;

namespace PaymentsApi.Tests;

public sealed class LocalInfrastructureTests
{
    [Fact]
    public async Task InMemoryModeRegistersCacheAndLockWithoutConnectingToRedisOrRabbitMq()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LocalTesting:UseInMemoryInfrastructure"] = "true",
                ["Jwt:Issuer"] = "PaymentsApi",
                ["Jwt:Audience"] = "PaymentsApi.Client",
                ["Jwt:Key"] = "0123456789abcdef0123456789abcdef",
                ["ConnectionStrings:Default"] = "Server=localhost,3306;Database=paymentsdb",
                ["RabbitMq:HostName"] = "localhost",
                ["RabbitMq:UserName"] = "guest",
                ["RabbitMq:Password"] = "guest",
                ["Cors:AllowedOrigins:0"] = "https://localhost"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationServices(configuration);

        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();
        await cache.SetStringAsync("local-test", "cache-ready");
        Assert.Equal("cache-ready", await cache.GetStringAsync("local-test"));
        Assert.Null(provider.GetService<IConnectionMultiplexer>());
        Assert.IsType<InMemoryIdempotencyLock>(provider.GetRequiredService<IIdempotencyLock>());
        var publisher = provider.GetRequiredService<ITransactionPublisher>();
        Assert.IsType<DisabledTransactionPublisher>(publisher);
        await publisher.PublishAsync(new Transaction
        {
            Reference = "local-test",
            Type = "Debit",
            Status = "Completed",
            IdempotencyKey = "local-test"
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InMemoryIdempotencyLockPreventsConcurrentUseOfSameKey()
    {
        var idempotencyLock = new InMemoryIdempotencyLock();
        var firstLease = await idempotencyLock.TryAcquireAsync("same-key", CancellationToken.None);

        Assert.NotNull(firstLease);
        Assert.Null(await idempotencyLock.TryAcquireAsync("same-key", CancellationToken.None));

        await firstLease.DisposeAsync();
        var nextLease = await idempotencyLock.TryAcquireAsync("same-key", CancellationToken.None);
        Assert.NotNull(nextLease);
        await nextLease.DisposeAsync();
    }
}
