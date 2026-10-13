namespace PaymentsApi.Models.Entities;

public sealed class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Reference { get; set; }
    public Guid AccountId { get; set; }
    public decimal Amount { get; set; }
    public required string Type { get; set; }
    public required string Status { get; set; }
    public required string IdempotencyKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Account? Account { get; set; }
}
