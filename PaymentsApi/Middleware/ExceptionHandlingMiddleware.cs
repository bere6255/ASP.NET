using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PaymentsApi.Exceptions;
using PaymentsApi.Models.Responses;

namespace PaymentsApi.Middleware;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException)
        {
            context.Response.Clear();
        }
        catch (AppException exception)
        {
            await WriteErrorAsync(context, exception.HttpStatus, exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            await WriteErrorAsync(context, StatusCodes.Status409Conflict, "Concurrent update, retry.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await WriteErrorAsync(context, StatusCodes.Status409Conflict, "Duplicate record");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled request failure for {Path}", context.Request.Path);
            await WriteErrorAsync(context, StatusCodes.Status500InternalServerError, "Something went wrong");
        }
    }

    private static async Task WriteErrorAsync(HttpContext context, int statusCode, string message)
    {
        if (context.Response.HasStarted)
            return;
        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(
            ApiResponse<object>.Fail(message, new { }), context.RequestAborted);
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException &&
        (sqlException.Number == 2601 || sqlException.Number == 2627);
}
