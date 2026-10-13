using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PaymentsApi.Config;
using PaymentsApi.Data;
using PaymentsApi.Models.Entities;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PaymentsApi.Messaging;

public sealed class TransactionConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    ILogger<TransactionConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "RabbitMQ consumer stopped unexpectedly; reconnecting.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        using var connection = new ConnectionFactory
        {
            HostName = settings.HostName,
            UserName = settings.UserName,
            Password = settings.Password,
            DispatchConsumersAsync = true
        }.CreateConnection();
        using var channel = connection.CreateModel();
        var retryQueue = settings.QueueName + ".retry";
        channel.QueueDeclare(settings.QueueName, durable: true, exclusive: false, autoDelete: false);
        channel.QueueDeclare(settings.DeadLetterQueueName, durable: true, exclusive: false, autoDelete: false);
        channel.QueueDeclare(retryQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object>
            {
                ["x-message-ttl"] = 5000,
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = settings.QueueName
            });
        channel.BasicQos(0, 1, false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (_, eventArgs) =>
        {
            var messageId = eventArgs.BasicProperties.MessageId;
            try
            {
                if (string.IsNullOrWhiteSpace(messageId))
                    throw new InvalidOperationException("Transaction message has no message ID.");
                await ProcessOnceAsync(messageId, stoppingToken);
                channel.BasicAck(eventArgs.DeliveryTag, multiple: false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                channel.BasicNack(eventArgs.DeliveryTag, multiple: false, requeue: true);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Transaction message processing failed; scheduling retry.");
                ScheduleRetry(channel, eventArgs, settings);
            }
        };
        channel.BasicConsume(settings.QueueName, autoAck: false, consumer);
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    private async Task ProcessOnceAsync(string messageId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await dbContext.ProcessedMessages.AnyAsync(message => message.MessageId == messageId, cancellationToken))
            return;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.ProcessedMessages.Add(new ProcessedMessage { MessageId = messageId });
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
    }

    private static void ScheduleRetry(IModel channel, BasicDeliverEventArgs eventArgs, RabbitMqOptions options)
    {
        var retries = eventArgs.BasicProperties.Headers?.TryGetValue("x-retry-count", out var value) == true
            ? Convert.ToInt32(value)
            : 0;
        var nextProperties = channel.CreateBasicProperties();
        nextProperties.Persistent = true;
        nextProperties.MessageId = eventArgs.BasicProperties.MessageId;
        nextProperties.ContentType = eventArgs.BasicProperties.ContentType;
        nextProperties.Headers = new Dictionary<string, object> { ["x-retry-count"] = retries + 1 };
        var destination = retries >= 2 ? options.DeadLetterQueueName : options.QueueName + ".retry";
        channel.BasicPublish(string.Empty, destination, nextProperties, eventArgs.Body);
        channel.BasicAck(eventArgs.DeliveryTag, multiple: false);
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException &&
        (sqlException.Number == 2601 || sqlException.Number == 2627);
}
