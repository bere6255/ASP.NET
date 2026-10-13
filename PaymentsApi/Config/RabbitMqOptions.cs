namespace PaymentsApi.Config;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";
    public required string HostName { get; init; }
    public required string UserName { get; init; }
    public required string Password { get; init; }
    public string QueueName { get; init; } = "transaction.completed";
    public string DeadLetterQueueName { get; init; } = "transaction.completed.dead";
}
