using System.Diagnostics;
using Merp.Application.Abstractions.Auditing;
using Merp.Application.Abstractions.Context;

namespace Merp.Infrastructure.Persistence.Auditing;

/// <summary>Writes <c>Read</c> audit entries to the module that maps the entity (D13).</summary>
internal sealed class SensitiveReadLog(IAuditWriter writer, ICurrentUser currentUser, TimeProvider timeProvider) : ISensitiveReadLog
{
    public Task RecordAsync(Type entityType, string entityId, IReadOnlyCollection<string> fields, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(fields);
        var now = timeProvider.GetUtcNow();
        var correlationId = Activity.Current?.TraceId.ToString();
        return writer.WriteAsync(
            entityType,
            [.. fields.Select(field => new AuditEntry
            {
                OccurredAt = now,
                UserId = currentUser.UserId,
                EntityType = entityType.Name,
                EntityId = entityId,
                Action = AuditAction.Read,
                Field = field,
                CorrelationId = correlationId,
            })],
            cancellationToken);
    }
}
