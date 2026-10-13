namespace PaymentsApi.Exceptions;

public sealed class UnauthorizedException(string message) : AppException(401, message);
