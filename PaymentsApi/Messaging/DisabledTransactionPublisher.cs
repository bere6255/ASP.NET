using PaymentsApi.Models.Entities;

namespace PaymentsApi.Messaging;

public sealed class DisabledTransactionPublisher(ILogger<DisabledTransactionPublisher> logger)
    : ITransactionPublisher
{
    public Task PublishAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation(
            "Skipped publishing transaction {TransactionId}; local in-memory infrastructure mode is enabled.",
            transaction.Id);
        return Task.CompletedTask;
    }
}
