namespace PaymentsApi.Exceptions;

public sealed class ForbiddenException(string message) : AppException(403, message);
