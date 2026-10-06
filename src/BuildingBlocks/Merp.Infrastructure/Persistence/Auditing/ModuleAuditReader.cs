using Merp.Application.Abstractions.Auditing;
using Merp.SharedKernel.Markers;
using Microsoft.EntityFrameworkCore;

namespace Merp.Infrastructure.Persistence.Auditing;

/// <summary>Reads the <c>audit_entries</c> table of one module; registered for every module DbContext.</summary>
internal sealed class ModuleAuditReader<TContext>(TContext db) : IAuditReader
    where TContext : ModuleDbContext, IModuleSchema
{
    public string Module => TContext.SchemaName;

    public IReadOnlyList<string> EntityTypes =>
        [.. db.Model.GetEntityTypes()
            .Where(t => !t.IsOwned() && t.ClrType.IsDefined(typeof(AuditedAttribute), inherit: true))
            .Select(t => t.ClrType.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    public async Task<IReadOnlyList<AuditRecord>> ReadAsync(AuditFilter filter, int take, CancellationToken cancellationToken)
    {
        var entries = await Query(filter)
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
        return [.. entries.Select(e => new AuditRecord(
            e.Id,
            TContext.SchemaName,
            e.OccurredAt,
            e.UserId,
            e.EntityType,
            e.EntityId,
            e.Action.ToString(),
            e.Field,
            e.OldValue,
            e.NewValue,
            e.CorrelationId))];
    }

    public Task<int> CountAsync(AuditFilter filter, CancellationToken cancellationToken) =>
        Query(filter).CountAsync(cancellationToken);

    private IQueryable<AuditEntry> Query(AuditFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = db.AuditEntries.AsNoTracking();
        if (filter.EntityType is { } entityType)
        {
            query = query.Where(e => e.EntityType == entityType);
        }

        if (filter.EntityId is { } entityId)
        {
            query = query.Where(e => e.EntityId == entityId);
        }

        if (filter.UserId is { } userId)
        {
            query = query.Where(e => e.UserId == userId);
        }

        if (filter.From is { } from)
        {
            query = query.Where(e => e.OccurredAt >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(e => e.OccurredAt <= to);
        }

        return query;
    }
}
