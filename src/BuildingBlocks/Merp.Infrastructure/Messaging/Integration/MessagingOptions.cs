namespace Merp.Infrastructure.Messaging.Integration;

public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";

    public MessagingTransport Transport { get; set; } = MessagingTransport.InProcess;

    /// <summary>Runs the outbox dispatcher and inbox processor in this process.</summary>
    public bool EnableBackgroundProcessing { get; set; } = true;

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);

    public int BatchSize { get; set; } = 50;

    /// <summary>Attempts before a message is moved to the dead letter state.</summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>Upper bound for the exponential retry delay.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(1);

    public RabbitMqOptions RabbitMq { get; set; } = new();

    public TimeSpan RetryDelay(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt), MaxRetryDelay.TotalSeconds));
}

public enum MessagingTransport
{
    InProcess,
    RabbitMQ,
}

public sealed class RabbitMqOptions
{
    public Uri Uri { get; set; } = new("amqp://merp:merp@localhost:5672");

    public string Exchange { get; set; } = "merp.events";

    /// <summary>Queue name prefix; each module consumes from <c>{prefix}{module}</c>.</summary>
    public string QueuePrefix { get; set; } = "merp.";

    public ushort PrefetchCount { get; set; } = 20;
}
