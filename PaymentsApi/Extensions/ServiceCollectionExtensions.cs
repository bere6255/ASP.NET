using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PaymentsApi.Config;
using PaymentsApi.Data;
using PaymentsApi.Filters;
using PaymentsApi.HealthChecks;
using PaymentsApi.Messaging;
using PaymentsApi.Models.Entities;
using PaymentsApi.Models.Responses;
using PaymentsApi.Repositories;
using PaymentsApi.Services;
using PaymentsApi.Validators;
using StackExchange.Redis;
using System.Threading.RateLimiting;

namespace PaymentsApi.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtSettings = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ??
            new JwtOptions
            {
                Issuer = "PaymentsApi",
                Audience = "PaymentsApi.Client",
                Key = "0123456789abcdef0123456789abcdef0123456789abcdef"
            };
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ??
            ["https://localhost"];
        var rateLimiting = configuration.GetSection("RateLimiting").Get<RateLimitingOptions>() ?? new();
        var useInMemoryInfrastructure = configuration.GetValue<bool>(
            "LocalTesting:UseInMemoryInfrastructure");

        services.AddDatabase(configuration);
        services.AddRedis(configuration, useInMemoryInfrastructure);
        services.AddMessaging(configuration, useInMemoryInfrastructure);
        services.AddApplicationComponents(allowedOrigins);
        services.AddJwtAuthentication(configuration, jwtSettings);
        services.AddCustomRateLimiter(rateLimiting);
        services.AddCustomHealthChecks(useInMemoryInfrastructure);
        services.AddCustomSwagger();

        return services;
    }

    public static IServiceCollection AddDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default") ??
            "Server=localhost,1433;Database=PaymentsDb;User Id=sa;******;TrustServerCertificate=True";
        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
        return services;
    }

    public static IServiceCollection AddRedis(
        this IServiceCollection services,
        IConfiguration configuration,
        bool useInMemoryInfrastructure = false)
    {
        if (useInMemoryInfrastructure)
        {
            services.AddDistributedMemoryCache();
            services.AddSingleton<IIdempotencyLock, InMemoryIdempotencyLock>();
            return services;
        }

        var redisConnectionString = configuration["Redis:ConnectionString"] ??
            "localhost:6379,abortConnect=false";
        services.AddSingleton<IConnectionMultiplexer>(
            _ => ConnectionMultiplexer.Connect(redisConnectionString));
        services.AddStackExchangeRedisCache(options => options.Configuration = redisConnectionString);
        services.AddSingleton<IIdempotencyLock, RedisIdempotencyLock>();
        return services;
    }

    public static IServiceCollection AddMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        bool useInMemoryInfrastructure = false)
    {
        services.Configure<RabbitMqOptions>(
            configuration.GetSection(RabbitMqOptions.SectionName));
        if (useInMemoryInfrastructure)
        {
            services.AddSingleton<ITransactionPublisher, DisabledTransactionPublisher>();
        }
        else
        {
            services.AddSingleton<ITransactionPublisher, RabbitMqPublisher>();
            services.AddHostedService<TransactionConsumer>();
        }
        return services;
    }

    public static IServiceCollection AddApplicationComponents(
        this IServiceCollection services,
        string[] allowedOrigins)
    {
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ITransferService, TransferService>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<ITokenIssuer, TokenIssuer>();
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<ApiResponseResultFilter>();
        services.AddScoped<FluentValidationActionFilter>();
        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();
        services.AddSingleton<IValidator<string>, IdempotencyKeyValidator>();

        services.AddControllers(options =>
        {
            options.Filters.AddService<FluentValidationActionFilter>();
            options.Filters.AddService<ApiResponseResultFilter>();
        }).ConfigureApiBehaviorOptions(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState
                    .Where(entry => entry.Value?.Errors.Count > 0)
                    .ToDictionary(entry => entry.Key.Length == 0
                            ? "request"
                            : char.ToLowerInvariant(entry.Key[0]) + entry.Key[1..],
                        entry => entry.Value!.Errors.Select(error =>
                            string.IsNullOrWhiteSpace(error.ErrorMessage)
                                ? "Invalid value."
                                : error.ErrorMessage).ToArray());
                return new BadRequestObjectResult(
                    ApiResponse<object>.Fail("Validation failed.", errors));
            };
        });

        services.AddCors(options => options.AddPolicy("strict", policy =>
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
        return services;
    }

    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        JwtOptions jwtSettings)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtSettings.Audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
                ClockSkew = TimeSpan.FromSeconds(30)
            };
            options.Events = new JwtBearerEvents
            {
                OnChallenge = async context =>
                {
                    context.HandleResponse();
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(
                        ApiResponse<object>.Fail("Authentication is required.", new { }),
                        context.HttpContext.RequestAborted);
                },
                OnForbidden = async context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(
                        ApiResponse<object>.Fail("Access is forbidden.", new { }),
                        context.HttpContext.RequestAborted);
                }
            };
        });
        services.AddAuthorization();
        return services;
    }

    public static IServiceCollection AddCustomRateLimiter(
        this IServiceCollection services,
        RateLimitingOptions rateLimiting)
    {
        services.AddRateLimiter(options =>
        {
            var authPermitLimit = Math.Max(rateLimiting.AuthPermitLimit, 1000);
            var transferPermitLimit = Math.Max(rateLimiting.TransferPermitLimit, 1000);
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsJsonAsync(
                    ApiResponse<object>.Fail("Too many requests.", new { }),
                    cancellationToken);
            };
            options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
                $"auth:{context.Connection.RemoteIpAddress}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = authPermitLimit,
                    Window = TimeSpan.FromMinutes(rateLimiting.WindowMinutes),
                    QueueLimit = 0
                }));
            options.AddPolicy("transfers", context =>
            {
                var subject = context.User.FindFirst("sub")?.Value ??
                    context.Connection.RemoteIpAddress?.ToString() ??
                    "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(
                    $"transfer:{subject}", _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = transferPermitLimit,
                        Window = TimeSpan.FromMinutes(rateLimiting.WindowMinutes),
                        QueueLimit = 0
                    });
            });
        });
        return services;
    }

    public static IServiceCollection AddCustomHealthChecks(
        this IServiceCollection services,
        bool useInMemoryInfrastructure = false)
    {
        var healthChecks = services.AddHealthChecks()
            .AddCheck<SqlHealthCheck>("sql");
        if (useInMemoryInfrastructure)
        {
            healthChecks.AddCheck(
                "local-cache",
                () => HealthCheckResult.Healthy("Using in-memory cache for local testing."));
        }
        else
        {
            healthChecks.AddCheck<RedisHealthCheck>("redis")
                .AddCheck<RabbitMqHealthCheck>("rabbitmq");
        }
        return services;
    }

    public static IServiceCollection AddCustomSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "Payments API", Version = "v1" });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });
        return services;
    }
}
