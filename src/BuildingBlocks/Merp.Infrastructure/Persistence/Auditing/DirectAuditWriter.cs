using Microsoft.Extensions.DependencyInjection;

namespace Merp.Infrastructure.Persistence.Auditing;

/// <summary>
/// Writes audit entries to the module that maps the entity, at once. A separate DbContext instance is used so the
/// entries do not depend on the request's unit of work: what was seen or exported stays recorded even if the request
/// fails afterwards.
/// </summary>
internal interface IAuditWriter
{
    Task WriteAsync(Type entityType, IReadOnlyCollection<AuditEntry> entries, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IAuditWriter"/>
internal sealed class DirectAuditWriter(IServiceProvider services, IEnumerable<ModuleDatabase> modules) : IAuditWriter
{
    public async Task WriteAsync(Type entityType, IReadOnlyCollection<AuditEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return;
        }

        var module = modules.FirstOrDefault(m => ((ModuleDbContext)services.GetRequiredService(m.DbContextType)).Model.FindEntityType(entityType) is not null)
            ?? throw new InvalidOperationException($"No module DbContext maps {entityType.Name}; it cannot be audited.");

        await using var db = (ModuleDbContext)ActivatorUtilities.CreateInstance(services, module.DbContextType);
        db.AuditEntries.AddRange(entries);
        await db.SaveChangesAsync(cancellationToken);
    }
}
