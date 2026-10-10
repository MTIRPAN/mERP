using System.Globalization;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Jobs;
using Merp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Merp.Infrastructure.Files;

/// <summary>
/// Nightly clean-up of file content (D31), per tenant and module:
/// 1. Content of deleted files is removed (normally already done right after the delete) and the row is stamped.
/// 2. Content without a metadata row, left by rolled back commands or interrupted writes, is removed once it is older
///    than <see cref="FileStorageOptions.OrphanGracePeriod"/>.
/// </summary>
public sealed partial class StoredFileSweepJob(ITenantJobRunner tenants, ILogger<StoredFileSweepJob> logger) : IRecurringJob
{
    public const string JobId = "files.sweep";
    private const int BatchSize = 500;

    public Task RunAsync(CancellationToken cancellationToken) =>
        tenants.RunForEachTenantAsync(SweepTenantAsync, cancellationToken);

    internal async Task SweepTenantAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var storage = services.GetRequiredService<IFileStorage>();
        var time = services.GetRequiredService<TimeProvider>();
        var options = services.GetRequiredService<IOptions<FileStorageOptions>>().Value;
        var tenant = services.GetRequiredService<ITenantContext>().RequiredTenant;

        foreach (var module in services.GetServices<ModuleDatabase>())
        {
            var db = (ModuleDbContext)services.GetRequiredService(module.DbContextType);
            var purged = await PurgeDeletedAsync(db, storage, time, cancellationToken);
            var orphans = await RemoveOrphansAsync(
                db,
                storage,
                string.Create(CultureInfo.InvariantCulture, $"{tenant.Id:N}/{module.Schema}/"),
                time.GetUtcNow() - options.OrphanGracePeriod,
                cancellationToken);

            if (purged + orphans > 0)
            {
                LogSwept(logger, tenant.Code, module.Schema, purged, orphans);
            }
        }
    }

    private static async Task<int> PurgeDeletedAsync(ModuleDbContext db, IFileStorage storage, TimeProvider time, CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            var files = await db.StoredFiles.IgnoreQueryFilters()
                .Where(f => f.IsDeleted && f.PurgedAt == null)
                .OrderBy(f => f.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (files.Count == 0)
            {
                return total;
            }

            foreach (var file in files)
            {
                await storage.DeleteAsync(file.StorageKey, cancellationToken);
                file.MarkPurged(time.GetUtcNow());
            }

            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
            total += files.Count;
        }
    }

    private static async Task<int> RemoveOrphansAsync(
        ModuleDbContext db,
        IFileStorage storage,
        string prefix,
        DateTimeOffset olderThan,
        CancellationToken cancellationToken)
    {
        var removed = 0;
        var batch = new List<string>(BatchSize);
        await foreach (var item in storage.ListAsync(prefix, cancellationToken))
        {
            if (item.LastModified >= olderThan)
            {
                continue;
            }

            batch.Add(item.Key);
            if (batch.Count == BatchSize)
            {
                removed += await RemoveUnknownAsync(db, storage, batch, cancellationToken);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            removed += await RemoveUnknownAsync(db, storage, batch, cancellationToken);
        }

        return removed;
    }

    private static async Task<int> RemoveUnknownAsync(ModuleDbContext db, IFileStorage storage, List<string> keys, CancellationToken cancellationToken)
    {
        // Deleted rows count as known: their content is purged by the first step, not here
        var known = await db.StoredFiles.IgnoreQueryFilters()
            .Where(f => keys.Contains(f.StorageKey))
            .Select(f => f.StorageKey)
            .ToListAsync(cancellationToken);
        var unknown = keys.Except(known, StringComparer.Ordinal).ToList();
        foreach (var key in unknown)
        {
            await storage.DeleteAsync(key, cancellationToken);
        }

        return unknown.Count;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "File sweep of {TenantCode}/{Module}: {Purged} deleted and {Orphans} orphaned files removed from storage")]
    private static partial void LogSwept(ILogger logger, string tenantCode, string module, int purged, int orphans);
}
