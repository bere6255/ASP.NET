namespace PaymentsApi.Models.Entities;

public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public Guid FamilyId { get; set; }
    public bool Used { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public User? User { get; set; }
}
