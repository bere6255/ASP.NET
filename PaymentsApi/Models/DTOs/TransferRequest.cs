namespace PaymentsApi.Models.DTOs;

public sealed record TransferRequest(Guid FromAccountId, Guid ToAccountId, decimal Amount);
