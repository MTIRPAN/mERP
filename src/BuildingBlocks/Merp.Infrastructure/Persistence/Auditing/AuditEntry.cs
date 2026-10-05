namespace Merp.Infrastructure.Persistence.Auditing;

/// <summary>One field change (or read) of an <c>[Audited]</c> entity (D13).</summary>
public sealed class AuditEntry
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>Platform user id; null for system changes.</summary>
    public Guid? UserId { get; init; }

    /// <summary>CLR type name of the entity, e.g. <c>Company</c>.</summary>
    public required string EntityType { get; init; }

    public required string EntityId { get; init; }

    public required AuditAction Action { get; init; }

    /// <summary>Property name; null for whole-entity actions such as delete.</summary>
    public string? Field { get; init; }

    public string? OldValue { get; init; }

    public string? NewValue { get; init; }

    /// <summary>Trace id of the request, to group the changes of one operation.</summary>
    public string? CorrelationId { get; init; }
}

public enum AuditAction
{
    Insert,
    Update,
    Delete,
    Read,
}
