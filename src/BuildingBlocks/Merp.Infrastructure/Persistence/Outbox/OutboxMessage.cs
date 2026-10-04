namespace Merp.Infrastructure.Persistence.Outbox;

/// <summary>Integration event written in the same transaction as the business data (D8).</summary>
public sealed class OutboxMessage
{
    /// <summary>Equals the integration event id.</summary>
    public Guid Id { get; init; }

    /// <summary>Registered event type name.</summary>
    public required string Type { get; init; }

    /// <summary>JSON payload (jsonb).</summary>
    public required string Payload { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>W3C trace parent of the publishing operation, used to continue the trace when dispatching.</summary>
    public string? TraceParent { get; init; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    public string? LastError { get; set; }

    /// <summary>Set when the message exceeded the retry limit; it is no longer dispatched.</summary>
    public DateTimeOffset? DeadLetteredAt { get; set; }
}
