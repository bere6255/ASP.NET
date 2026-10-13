using Microsoft.EntityFrameworkCore;
using PaymentsApi.Models.Entities;

namespace PaymentsApi.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(user => user.Id);
            entity.Property(user => user.NormalizedEmail).HasMaxLength(320).IsRequired();
            entity.Property(user => user.PasswordHash).IsRequired();
            entity.Property(user => user.Role).HasMaxLength(64).IsRequired();
            // The database uniqueness constraint is the final duplicate defense when parallel requests race.
            entity.HasIndex(user => user.NormalizedEmail).IsUnique();
        });

        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(account => account.Id);
            entity.Property(account => account.Balance).HasPrecision(18, 2);
            entity.Property(account => account.RowVersion).IsRowVersion();
            entity.HasOne(account => account.User).WithMany(user => user.Accounts)
                .HasForeignKey(account => account.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(account => account.UserId);
        });

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(transaction => transaction.Id);
            entity.Property(transaction => transaction.Amount).HasPrecision(18, 2);
            entity.Property(transaction => transaction.Reference).HasMaxLength(64).IsRequired();
            entity.Property(transaction => transaction.Type).HasMaxLength(32).IsRequired();
            entity.Property(transaction => transaction.Status).HasMaxLength(32).IsRequired();
            entity.Property(transaction => transaction.IdempotencyKey).HasMaxLength(128).IsRequired();
            entity.HasOne(transaction => transaction.Account).WithMany(account => account.Transactions)
                .HasForeignKey(transaction => transaction.AccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(transaction => transaction.AccountId);
            // The unique database index prevents duplicate references even across concurrent writers.
            entity.HasIndex(transaction => transaction.Reference).IsUnique();
        });

        modelBuilder.Entity<IdempotencyKey>(entity =>
        {
            entity.HasKey(key => key.Key);
            entity.Property(key => key.Key).HasMaxLength(128);
            entity.Property(key => key.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(key => key.ResponseBody).IsRequired();
            // The database is authoritative when simultaneous requests bypass the Redis fast path.
            entity.HasIndex(key => key.Key).IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(token => token.Id);
            entity.Property(token => token.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasOne(token => token.User).WithMany(user => user.RefreshTokens)
                .HasForeignKey(token => token.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(token => token.UserId);
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.HasIndex(token => new { token.FamilyId, token.Used });
        });

        modelBuilder.Entity<ProcessedMessage>(entity =>
        {
            entity.HasKey(message => message.MessageId);
            entity.Property(message => message.MessageId).HasMaxLength(128);
        });
    }
}
