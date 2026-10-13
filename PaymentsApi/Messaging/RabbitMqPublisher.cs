using System.Text.Json;
using Microsoft.Extensions.Options;
using PaymentsApi.Config;
using PaymentsApi.Messaging;
using PaymentsApi.Models.Entities;
using RabbitMQ.Client;

namespace PaymentsApi.Messaging;

public sealed class RabbitMqPublisher : ITransactionPublisher, IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly RabbitMqOptions _options;

    public RabbitMqPublisher(IOptions<RabbitMqOptions> options)
    {
        _options = options.Value;
        _connection = new ConnectionFactory
        {
            HostName = _options.HostName,
            UserName = _options.UserName,
            Password = _options.Password,
            DispatchConsumersAsync = true
        }.CreateConnection();
        _channel = _connection.CreateModel();
        _channel.ConfirmSelect();
        _channel.QueueDeclare(_options.QueueName, durable: true, exclusive: false, autoDelete: false);
        _channel.QueueDeclare(_options.DeadLetterQueueName, durable: true, exclusive: false, autoDelete: false);
    }

    public Task PublishAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var message = new
        {
            MessageId = transaction.Id.ToString(),
            transaction.Reference,
            transaction.AccountId,
            transaction.Amount,
            transaction.Type,
            transaction.CreatedAt
        };
        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.MessageId = transaction.Id.ToString();
        properties.ContentType = "application/json";
        _channel.BasicPublish(string.Empty, _options.QueueName, properties,
            JsonSerializer.SerializeToUtf8Bytes(message));
        _channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _channel.Dispose();
        _connection.Dispose();
    }
}
