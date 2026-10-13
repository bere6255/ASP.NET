namespace PaymentsApi.Models.Responses;

public sealed record TransferReceipt(string Reference, decimal Amount, string Status);
