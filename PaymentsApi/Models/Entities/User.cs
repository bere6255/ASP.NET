namespace PaymentsApi.Models.Entities;

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string NormalizedEmail { get; set; }
    public required string PasswordHash { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public string Role { get; set; } = "Customer";
    public ICollection<Account> Accounts { get; set; } = new List<Account>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
