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

    /// <summary>What made the change when it is not a direct edit, e.g. <c>hr.profile-change:PDT-2026-00012</c> (D39).</summary>
    public string? Source { get; init; }
}

public enum AuditAction
{
    Insert,
    Update,
    Delete,
    Read,

    /// <summary>A list was exported to Excel (D35): NewValue holds the row count, columns, sort and filters.</summary>
    Export,
}
