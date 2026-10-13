namespace PaymentsApi.Exceptions;

public sealed class ConflictException(string message) : AppException(409, message);
