using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PaymentsApi.Models.Responses;

namespace PaymentsApi.Filters;

public sealed class ApiResponseResultFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult objectResult &&
            objectResult.StatusCode is >= 200 and < 300 &&
            objectResult.Value is not null &&
            !IsApiResponse(objectResult.Value.GetType()))
        {
            context.Result = new ObjectResult(ApiResponse<object>.Ok(objectResult.Value))
            {
                StatusCode = objectResult.StatusCode
            };
        }

        await next();
    }

    private static bool IsApiResponse(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ApiResponse<>);
}
