namespace Merp.Application.Abstractions.Auditing;

/// <summary>
/// Reads one module's audit entries (D13). Every module with a DbContext registers one, so Platform can search the
/// audit trail of all modules without referencing them.
/// </summary>
public interface IAuditReader
{
    /// <summary>Module (schema) name, e.g. <c>platform</c>.</summary>
    string Module { get; }

    /// <summary>Entity types of the module that are audited, by CLR name (<c>Company</c>).</summary>
    IReadOnlyList<string> EntityTypes { get; }

    /// <summary>The newest <paramref name="take"/> entries matching the filter, newest first.</summary>
    Task<IReadOnlyList<AuditRecord>> ReadAsync(AuditFilter filter, int take, CancellationToken cancellationToken);

    Task<int> CountAsync(AuditFilter filter, CancellationToken cancellationToken);
}

/// <summary>All conditions are optional; the period is inclusive on both ends.</summary>
public sealed record AuditFilter(
    string? EntityType = null,
    string? EntityId = null,
    Guid? UserId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);

/// <remarks>
/// Action is Insert, Update, Delete or Read. Field is the property name (<c>ShortName</c>, <c>Address.City</c>);
/// null for a whole-entity action. Source says what made a change that is not a direct edit (<see cref="IAuditSource"/>).
/// </remarks>
public sealed record AuditRecord(
    Guid Id,
    string Module,
    DateTimeOffset OccurredAt,
    Guid? UserId,
    string EntityType,
    string EntityId,
    string Action,
    string? Field,
    string? OldValue,
    string? NewValue,
    string? CorrelationId,
    string? Source = null);
