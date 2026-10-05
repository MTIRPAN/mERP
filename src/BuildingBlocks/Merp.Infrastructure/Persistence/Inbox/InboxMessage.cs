namespace Merp.Infrastructure.Persistence.Inbox;

/// <summary>
/// Received integration event per handler. The (message id, handler) pair is unique, so every handler processes
/// a message once even if it is delivered more than once (D8).
/// </summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; init; }

    /// <summary>Full name of the handler type.</summary>
    public required string Handler { get; init; }

    public required string Type { get; init; }

    public required string Payload { get; init; }

    public DateTimeOffset ReceivedAt { get; init; }

    public string? TraceParent { get; init; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset? DeadLetteredAt { get; set; }
}
