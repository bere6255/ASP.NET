namespace PaymentsApi.Models.Entities;

public sealed class IdempotencyKey
{
    public required string Key { get; set; }
    public required string RequestHash { get; set; }
    public required string ResponseBody { get; set; }
    public int StatusCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
