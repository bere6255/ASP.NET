namespace PaymentsApi.Exceptions;

public sealed class NotFoundException(string message) : AppException(404, message);
