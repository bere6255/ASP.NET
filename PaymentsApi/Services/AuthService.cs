using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using PaymentsApi.Data;
using PaymentsApi.Exceptions;
using PaymentsApi.Models.DTOs;
using PaymentsApi.Models.Entities;
using PaymentsApi.Models.Responses;
using PaymentsApi.Repositories;

namespace PaymentsApi.Services;

public sealed class AuthService(
    AppDbContext dbContext,
    IUserRepository users,
    IPasswordHasher<User> passwordHasher,
    ITokenIssuer tokenIssuer,
    ITokenService tokenService) : IAuthService
{
    public async Task<AuthTokens> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();

        // 1. Quick upfront check avoids heavy hashing work if the email already exists
        if (await users.ExistsByEmailAsync(normalizedEmail, cancellationToken))
        {
            throw new ConflictException("An account with that email already exists.");
        }

        var user = new User { NormalizedEmail = normalizedEmail, PasswordHash = string.Empty };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        
        var refresh = tokenService.CreateRefreshToken();
        var familyId = Guid.NewGuid();
        
        user.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokenService.Hash(refresh),
            FamilyId = familyId,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        });
        
        user.Accounts.Add(new Account { UserId = user.Id, Balance = 0m });

        try
        {
            // 2. Database unique constraint acts as a robust race-condition safety net
            await users.AddAsync(user, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new ConflictException("An account with that email already exists.");
        }

        return tokenIssuer.Issue(user, refresh);
    }

    public async Task<AuthTokens> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToUpperInvariant();
        var user = await users.FindByEmailAsync(email, cancellationToken);
        
        if (user is null)
        {
            throw new UnauthorizedException("Invalid email or password.");
        }

        // Check lockout *before* running expensive password hash verification
        if (user.LockoutEnd > DateTimeOffset.UtcNow)
        {
            throw new UnauthorizedException("Invalid email or password.");
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        
        if (verification == PasswordVerificationResult.Failed)
        {
            var now = DateTimeOffset.UtcNow;
            await dbContext.Users.Where(candidate => candidate.Id == user.Id)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(candidate => candidate.FailedAttempts, candidate => candidate.FailedAttempts + 1)
                    .SetProperty(candidate => candidate.LockoutEnd,
                        candidate => candidate.FailedAttempts >= 4 ? now.AddMinutes(15) : candidate.LockoutEnd),
                    cancellationToken);
                    
            throw new UnauthorizedException("Invalid email or password.");
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        }

        // Reset failure tracking state on successful login
        user.FailedAttempts = 0;
        user.LockoutEnd = null;

        var refresh = tokenService.CreateRefreshToken();
        var familyId = Guid.NewGuid();
        
        dbContext.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokenService.Hash(refresh),
            FamilyId = familyId,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        });

        // Single database round-trip to save the refresh token and reset failure state
        await dbContext.SaveChangesAsync(cancellationToken);
        
        return tokenIssuer.Issue(user, refresh);
    }

    public async Task<AuthTokens> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        var tokenHash = tokenService.Hash(request.RefreshToken);
        var now = DateTimeOffset.UtcNow;

        // Wrap the entire verification and rotation lifecycle in an explicit transaction
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existing = await dbContext.RefreshTokens.SingleOrDefaultAsync(
            token => token.TokenHash == tokenHash, cancellationToken);
            
        if (existing is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new UnauthorizedException("Invalid refresh token.");
        }

        var affected = await dbContext.RefreshTokens
            .Where(token => token.Id == existing.Id && !token.Used && token.ExpiresAt > now)
            .ExecuteUpdateAsync(update => update.SetProperty(token => token.Used, true), cancellationToken);
            
        if (affected != 1)
        {
            var wasUsed = await dbContext.RefreshTokens.AsNoTracking()
                .Where(token => token.Id == existing.Id)
                .Select(token => token.Used)
                .SingleAsync(cancellationToken);
                
            if (wasUsed)
            {
                // Theft detection triggered: invalidate the entire token family
                await dbContext.RefreshTokens
                    .Where(token => token.FamilyId == existing.FamilyId)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(token => token.Used, true)
                        .SetProperty(token => token.ExpiresAt, now), cancellationToken);
            }
            
            await transaction.CommitAsync(cancellationToken);
            throw new UnauthorizedException("Invalid refresh token.");
        }

        var user = await dbContext.Users.SingleAsync(candidate => candidate.Id == existing.UserId, cancellationToken);
        var nextRefresh = tokenService.CreateRefreshToken();
        
        dbContext.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokenService.Hash(nextRefresh),
            FamilyId = existing.FamilyId, // Preserves the family lineage
            ExpiresAt = now.AddDays(30)
        });
        
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        
        return tokenIssuer.Issue(user, nextRefresh);
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException &&
        (sqlException.Number == 2601 || sqlException.Number == 2627);
}