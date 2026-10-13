namespace PaymentsApi.Exceptions;

public sealed class ValidationException(string message) : AppException(400, message);
