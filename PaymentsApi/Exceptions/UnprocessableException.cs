namespace PaymentsApi.Exceptions;

public sealed class UnprocessableException(string message) : AppException(422, message);
