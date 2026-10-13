using PaymentsApi.Models.DTOs;
using PaymentsApi.Models.Responses;

namespace PaymentsApi.Services;

public interface ITransferService
{
    Task<(TransferReceipt Receipt, int StatusCode)> TransferAsync(
        Guid userId, string idempotencyKey, TransferRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> GetOwnedAccountIdsAsync(Guid userId, CancellationToken cancellationToken);
}
