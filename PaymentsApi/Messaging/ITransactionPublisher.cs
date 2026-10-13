using PaymentsApi.Models.Entities;

namespace PaymentsApi.Messaging;

public interface ITransactionPublisher
{
    Task PublishAsync(Transaction transaction, CancellationToken cancellationToken);
}
