namespace PaymentsApi.Models.Entities;

public sealed class ProcessedMessage
{
    public required string MessageId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; } = DateTimeOffset.UtcNow;
}
