using Microsoft.Extensions.Diagnostics.HealthChecks;
using PaymentsApi.Models.Responses;
using PaymentsApi.Middleware;

namespace PaymentsApi.Extensions;

public static class ApplicationBuilderExtensions
{
    public static WebApplication UseApplicationPipeline(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseRouting();
        app.UseStatusCodePages(async statusContext =>
        {
            var response = statusContext.HttpContext.Response;
            if (response.HasStarted)
                return;
            var message = response.StatusCode switch
            {
                StatusCodes.Status404NotFound => "Resource not found.",
                StatusCodes.Status405MethodNotAllowed => "Method not allowed.",
                _ => "Request failed."
            };
            response.ContentType = "application/json";
            await response.WriteAsJsonAsync(ApiResponse<object>.Fail(message, new { }));
        });
        if (!app.Environment.IsDevelopment())
            app.UseHsts();
        app.UseHttpsRedirection();
        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseCors("strict");
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        app.MapControllers();
        app.MapGet("/health", async (
            HealthCheckService healthChecks,
            CancellationToken cancellationToken) =>
        {
            var report = await healthChecks.CheckHealthAsync(cancellationToken);
            return Results.Json(ApiResponse<object>.Ok(new
            {
                status = report.Status.ToString(),
                checks = report.Entries.ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value.Status.ToString())
            }), statusCode: report.Status == HealthStatus.Healthy
                ? StatusCodes.Status200OK
                : StatusCodes.Status503ServiceUnavailable);
        });

        return app;
    }
}
