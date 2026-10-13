using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using PaymentsApi.Data;
using PaymentsApi.Exceptions;
using PaymentsApi.Messaging;
using PaymentsApi.Models.DTOs;
using PaymentsApi.Models.Entities;
using PaymentsApi.Models.Responses;

namespace PaymentsApi.Services;

public sealed class TransferService(
    AppDbContext dbContext,
    IDistributedCache cache,
    IIdempotencyLock idempotencyLock,
    ITransactionPublisher publisher) : ITransferService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);

    public async Task<IReadOnlyList<Guid>> GetOwnedAccountIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.Accounts.AsNoTracking().Where(account => account.UserId == userId)
            .Select(account => account.Id).ToListAsync(cancellationToken);

    public async Task<(TransferReceipt Receipt, int StatusCode)> TransferAsync(
        Guid userId, string idempotencyKey, TransferRequest request, CancellationToken cancellationToken)
    {
        var requestHash = HashRequest(request);
        var cached = await cache.GetStringAsync(CacheKey(idempotencyKey), cancellationToken);
        if (cached is not null)
            return Decode(cached, requestHash);

        var databaseResult = await FindStoredResultAsync(idempotencyKey, requestHash, cancellationToken);
        if (databaseResult is not null)
        {
            await CacheResultAsync(idempotencyKey, requestHash, databaseResult.Value, cancellationToken);
            return databaseResult.Value;
        }

        var lockKey = $"idempotency-lock:{idempotencyKey}";
        await using var lockLease = await idempotencyLock.TryAcquireAsync(lockKey, cancellationToken);
        if (lockLease is null)
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
                cached = await cache.GetStringAsync(CacheKey(idempotencyKey), cancellationToken);
                if (cached is not null)
                    return Decode(cached, requestHash);
                databaseResult = await FindStoredResultAsync(idempotencyKey, requestHash, cancellationToken);
                if (databaseResult is not null)
                    return databaseResult.Value;
            }
            throw new ConflictException("A request with this idempotency key is still processing.");
        }

        try
        {
            var existing = await FindStoredResultAsync(idempotencyKey, requestHash, cancellationToken);
            if (existing is not null)
                return existing.Value;

            var accounts = await dbContext.Accounts
                .Where(account => account.Id == request.FromAccountId || account.Id == request.ToAccountId)
                .OrderBy(account => account.Id)
                .ToListAsync(cancellationToken);
            var source = accounts.SingleOrDefault(account => account.Id == request.FromAccountId);
            var destination = accounts.SingleOrDefault(account => account.Id == request.ToAccountId);
            if (source is null || destination is null)
                throw new NotFoundException("One or more accounts were not found.");
            if (source.UserId != userId)
                throw new ForbiddenException("Transfers are only allowed from your own accounts.");
            if (source.Balance < request.Amount)
                throw new UnprocessableException("Insufficient funds.");

            var reference = Guid.NewGuid().ToString("N");
            var outgoing = NewTransaction(reference, source.Id, request.Amount, "Debit", idempotencyKey);
            var incoming = NewTransaction(reference + "-C", destination.Id, request.Amount, "Credit", idempotencyKey);
            source.Balance -= request.Amount;
            destination.Balance += request.Amount;
            var receipt = new TransferReceipt(reference, request.Amount, "Completed");
            var response = ApiResponse<TransferReceipt>.Ok(receipt, "Transfer completed.");
            var responseBody = JsonSerializer.Serialize(response);
            var result = (Receipt: receipt, StatusCode: StatusCodes.Status201Created);

            await using (var sqlTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
            {
                try
                {
                    dbContext.Transactions.AddRange(outgoing, incoming);
                    dbContext.IdempotencyKeys.Add(new IdempotencyKey
                    {
                        Key = idempotencyKey,
                        RequestHash = requestHash,
                        ResponseBody = responseBody,
                        StatusCode = result.StatusCode
                    });
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await sqlTransaction.CommitAsync(cancellationToken);
                }
                catch
                {
                    await sqlTransaction.RollbackAsync(CancellationToken.None);
                    throw;
                }
            }

            await CacheResultAsync(idempotencyKey, requestHash, result, cancellationToken);
            // An outbox would atomically persist the publish intent with the transfer to close this crash window.
            await publisher.PublishAsync(outgoing, cancellationToken);
            await publisher.PublishAsync(incoming, cancellationToken);
            return result;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            var original = await FindStoredResultAsync(idempotencyKey, requestHash, cancellationToken);
            if (original is not null)
            {
                await CacheResultAsync(idempotencyKey, requestHash, original.Value, cancellationToken);
                return original.Value;
            }
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Concurrent update, retry.");
        }
    }

    private async Task<(TransferReceipt Receipt, int StatusCode)?> FindStoredResultAsync(
        string key, string requestHash, CancellationToken cancellationToken)
    {
        var stored = await dbContext.IdempotencyKeys.AsNoTracking()
            .SingleOrDefaultAsync(entry => entry.Key == key, cancellationToken);
        if (stored is null)
            return null;
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(stored.RequestHash), Convert.FromHexString(requestHash)))
            throw new UnprocessableException("Idempotency key was already used for a different request.");
        var response = JsonSerializer.Deserialize<ApiResponse<TransferReceipt>>(stored.ResponseBody)
            ?? throw new InvalidOperationException("Stored idempotency response is invalid.");
        return (response.Data, stored.StatusCode);
    }

    private async Task CacheResultAsync(
        string key, string requestHash, (TransferReceipt Receipt, int StatusCode) result,
        CancellationToken cancellationToken)
    {
        var value = JsonSerializer.Serialize(new CachedResult(requestHash, result.Receipt, result.StatusCode));
        await cache.SetStringAsync(CacheKey(key), value, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheLifetime
        }, cancellationToken);
    }

    private static (TransferReceipt Receipt, int StatusCode) Decode(string value, string requestHash)
    {
        var cached = JsonSerializer.Deserialize<CachedResult>(value)
            ?? throw new InvalidOperationException("Cached idempotency response is invalid.");
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(cached.RequestHash), Convert.FromHexString(requestHash)))
            throw new UnprocessableException("Idempotency key was already used for a different request.");
        return (cached.Receipt, cached.StatusCode);
    }

    private static Transaction NewTransaction(string reference, Guid accountId, decimal amount, string type, string key) =>
        new()
        {
            Reference = reference,
            AccountId = accountId,
            Amount = amount,
            Type = type,
            Status = "Completed",
            IdempotencyKey = key
        };

    private static string HashRequest(TransferRequest request)
    {
        var canonical = string.Join("|", request.FromAccountId.ToString("N"),
            request.ToAccountId.ToString("N"), request.Amount.ToString("0.00", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string CacheKey(string key) => $"idempotency:{key}";

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException &&
        (sqlException.Number == 2601 || sqlException.Number == 2627);

    private sealed record CachedResult(string RequestHash, TransferReceipt Receipt, int StatusCode);
}
