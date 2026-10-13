using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PaymentsApi.Exceptions;
using PaymentsApi.Models.DTOs;
using PaymentsApi.Models.Responses;
using PaymentsApi.Services;
using Xunit;

namespace PaymentsApi.Tests;

public sealed class ApiContractTests : IClassFixture<TestFactory>
{
    private readonly HttpClient client;
    private readonly TestFactory factory;
    private readonly TestState state;

    public ApiContractTests(TestFactory factory)
    {
        this.factory = factory;
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        state = factory.State;
    }

    [Fact]
    public async Task SuccessfulObjectResultUsesApiResponseEnvelope()
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("user@example.com", "Password!12345"));
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<AuthTokens>>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body!.Status);
        Assert.NotNull(body.Data.AccessToken);
        Assert.Equal("Success", body.Message);
    }

    [Fact]
    public async Task ValidationFailureHasFieldErrorsInEnvelope()
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("bad", "short"));
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<Dictionary<string, string[]>>>();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(body!.Status);
        Assert.Contains("email", body.Data.Keys);
        Assert.Contains("password", body.Data.Keys);
    }

    [Fact]
    public async Task ProtectedTransferReturnsEnvelopeOnUnauthorized()
    {
        var response = await client.PostAsJsonAsync("/api/transfers",
            new TransferRequest(Guid.NewGuid(), Guid.NewGuid(), 1m));
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(body!.Status);
        Assert.NotNull(body.Message);
    }

    [Fact]
    public async Task MissingRouteReturns404Envelope()
    {
        var response = await client.GetAsync("/missing-route");
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(body!.Status);
    }

    [Fact]
    public async Task ConcurrentRegistrationCreatesOnlyOneAccount()
    {
        var requests = Enumerable.Range(0, 6).Select(_ =>
            client.PostAsJsonAsync("/api/auth/register",
                new RegisterRequest("race@example.com", "StrongPassword!123")));
        var responses = await Task.WhenAll(requests);
        Assert.Single(responses.Where(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(5, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, state.RegisteredUsers.Count);
    }

    [Fact]
    public async Task ConcurrentRefreshConsumesTokenOnce()
    {
        var requests = Enumerable.Range(0, 2).Select(_ =>
            client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest("single-use-token")));
        var responses = await Task.WhenAll(requests);
        Assert.Single(responses.Where(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Single(responses.Where(response => response.StatusCode == HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task ConcurrentDuplicateTransferExecutesOnceAndReplaysReceipt()
    {
        using var authenticatedClient = CreateAuthenticatedClient();
        var request = new TransferRequest(Guid.NewGuid(), Guid.NewGuid(), 10m);
        var calls = Enumerable.Range(0, 6).Select(_ =>
        {
            var message = new HttpRequestMessage(HttpMethod.Post, "/api/transfers")
            {
                Content = JsonContent.Create(request)
            };
            message.Headers.Add("Idempotency-Key", "concurrent-transfer-001");
            return authenticatedClient.SendAsync(message);
        });
        var responses = await Task.WhenAll(calls);
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        Assert.Equal(1, state.TransferExecutions);
    }

    [Fact]
    public async Task DuplicateRegistrationReturns409Envelope()
    {
        _ = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("existing@example.com", "StrongPassword!123"));
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("existing@example.com", "StrongPassword!123"));
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False(body!.Status);
    }

    [Fact]
    public async Task UnhandledFailureHidesInternalMessage()
    {
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("explode@example.com", "StrongPassword!123"));
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("Something went wrong", content);
        Assert.DoesNotContain("sensitive internal detail", content);
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var authenticatedClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        authenticatedClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestFactory.CreateToken());
        return authenticatedClient;
    }
}

public sealed class TestFactory : WebApplicationFactory<Program>
{
    private const string TestJwtKey = "Q4z!nP8v@X2cL6mR9sW3yF7hK1dB5gJ8";
    public TestState State { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Server=localhost;Database=unused;User Id=sa;Password=Unused_password123!;TrustServerCertificate=True",
                ["Redis:ConnectionString"] = "localhost:6379,abortConnect=false",
                ["RabbitMq:HostName"] = "localhost",
                ["RabbitMq:UserName"] = "test",
                ["RabbitMq:Password"] = "test-password",
                ["RabbitMq:QueueName"] = "transaction.completed",
                ["RabbitMq:DeadLetterQueueName"] = "transaction.completed.dead",
                ["Jwt:Issuer"] = "PaymentsApi",
                ["Jwt:Audience"] = "PaymentsApi.Client",
                ["Jwt:Key"] = TestJwtKey,
                ["Cors:AllowedOrigins:0"] = "https://localhost",
                ["RateLimiting:AuthPermitLimit"] = "1000",
                ["RateLimiting:TransferPermitLimit"] = "1000"
            }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAuthService>();
            services.RemoveAll<ITransferService>();
            services.RemoveAll<IHostedService>();
            services.AddSingleton(State);
            services.AddSingleton<IAuthService, FakeAuthService>();
            services.AddSingleton<ITransferService, FakeTransferService>();
        });
    }

    public static string CreateToken()
    {
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtKey));
        var credentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken("PaymentsApi", "PaymentsApi.Client",
            [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(10), credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public sealed class TestState
{
    public ConcurrentDictionary<string, byte> RegisteredUsers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int TransferExecutions;
    public int RefreshConsumed;
    public object TransferLock { get; } = new();
    public ConcurrentDictionary<string, TransferReceipt> Transfers { get; } = new();
}

public sealed class FakeAuthService(TestState state) : IAuthService
{
    public Task<AuthTokens> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        if (!state.RegisteredUsers.TryAdd(request.Email, 0))
            throw new ConflictException("An account with that email already exists.");
        return Task.FromResult(new AuthTokens("test-access-token", "test-refresh-token", DateTimeOffset.UtcNow.AddMinutes(15)));
    }

    public Task<AuthTokens> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        if (request.Email == "explode@example.com")
            throw new Exception("sensitive internal detail");
        return Task.FromResult(new AuthTokens("test-access-token", "test-refresh-token", DateTimeOffset.UtcNow.AddMinutes(15)));
    }

    public Task<AuthTokens> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref state.RefreshConsumed, 1) != 0)
            throw new PaymentsApi.Exceptions.UnauthorizedException("Invalid refresh token.");
        return Task.FromResult(new AuthTokens("new-access-token", "new-refresh-token", DateTimeOffset.UtcNow.AddMinutes(15)));
    }
}

public sealed class FakeTransferService(TestState state) : ITransferService
{
    public Task<IReadOnlyList<Guid>> GetOwnedAccountIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);

    public Task<(TransferReceipt Receipt, int StatusCode)> TransferAsync(
        Guid userId, string idempotencyKey, TransferRequest request, CancellationToken cancellationToken)
    {
        lock (state.TransferLock)
        {
            if (!state.Transfers.TryGetValue(idempotencyKey, out var receipt))
            {
                Interlocked.Increment(ref state.TransferExecutions);
                receipt = new TransferReceipt(Guid.NewGuid().ToString("N"), request.Amount, "Completed");
                state.Transfers[idempotencyKey] = receipt;
            }
            return Task.FromResult((receipt, (int)HttpStatusCode.Created));
        }
    }
}
