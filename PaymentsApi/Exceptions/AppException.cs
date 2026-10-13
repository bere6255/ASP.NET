namespace PaymentsApi.Exceptions;

public class AppException(int httpStatus, string message) : Exception(message)
{
    public int HttpStatus { get; } = httpStatus;
}
