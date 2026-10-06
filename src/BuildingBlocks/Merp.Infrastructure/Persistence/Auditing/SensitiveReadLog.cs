using System.Diagnostics;
using Merp.Application.Abstractions.Auditing;
using Merp.Application.Abstractions.Context;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Infrastructure.Persistence.Auditing;

/// <summary>
/// Writes <c>Read</c> audit entries to the module that maps the entity. A separate DbContext instance is used so the
/// entries are saved at once, without committing or depending on the request's unit of work.
/// </summary>
internal sealed class SensitiveReadLog(
    IServiceProvider services,
    IEnumerable<ModuleDatabase> modules,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ISensitiveReadLog
{
    public async Task RecordAsync(Type entityType, string entityId, IReadOnlyCollection<string> fields, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Count == 0)
        {
            return;
        }

        var module = modules.FirstOrDefault(m => ((ModuleDbContext)services.GetRequiredService(m.DbContextType)).Model.FindEntityType(entityType) is not null)
            ?? throw new InvalidOperationException($"No module DbContext maps {entityType.Name}; its reads cannot be audited.");

        await using var db = (ModuleDbContext)ActivatorUtilities.CreateInstance(services, module.DbContextType);
        var now = timeProvider.GetUtcNow();
        var correlationId = Activity.Current?.TraceId.ToString();
        db.AuditEntries.AddRange(fields.Select(field => new AuditEntry
        {
            OccurredAt = now,
            UserId = currentUser.UserId,
            EntityType = entityType.Name,
            EntityId = entityId,
            Action = AuditAction.Read,
            Field = field,
            CorrelationId = correlationId,
        }));
        await db.SaveChangesAsync(cancellationToken);
    }
}
