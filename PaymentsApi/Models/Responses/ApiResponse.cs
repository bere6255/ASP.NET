namespace PaymentsApi.Models.Responses;

public sealed record ApiResponse<T>(bool Status, T Data, string Message)
{
    public static ApiResponse<T> Ok(T data, string message = "Success") => new(true, data, message);
    public static ApiResponse<T> Fail(string message, T data) => new(false, data, message);
}
