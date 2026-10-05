using Merp.Infrastructure.MultiTenancy.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Merp.Infrastructure.MultiTenancy;

/// <summary>
/// Tenant lookups backed by the catalog database and cached in memory. Decrypted connection strings never reach
/// the distributed cache.
/// </summary>
public sealed class CatalogTenantStore(
    IDbContextFactory<CatalogDbContext> dbContextFactory,
    TenantConnectionStringProtector protector,
    HybridCache cache) : ITenantStore
{
    private const string CacheTag = "catalog:tenants";

    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(5),
        Flags = HybridCacheEntryFlags.DisableDistributedCache,
    };

    private static readonly string[] Tags = [CacheTag];

    public async Task<TenantRecord?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);
        var normalized = Tenant.NormalizeCode(code);
        if (!Tenant.IsValidCode(normalized))
        {
            return null;
        }

        var tenants = await GetAllAsync(cancellationToken);
        return tenants.FirstOrDefault(t => string.Equals(t.Code, normalized, StringComparison.Ordinal));
    }

    public async Task<TenantRecord?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenants = await GetAllAsync(cancellationToken);
        return tenants.FirstOrDefault(t => t.Id == id);
    }

    public async Task<IReadOnlyList<TenantRecord>> GetActiveTenantsAsync(CancellationToken cancellationToken = default)
    {
        var tenants = await GetAllAsync(cancellationToken);
        return [.. tenants.Where(t => t.IsActive)];
    }

    public async Task InvalidateAsync(CancellationToken cancellationToken = default) =>
        await cache.RemoveByTagAsync(CacheTag, cancellationToken);

    // The catalog is small (one row per customer), so the whole list is cached as one entry.
    private async Task<IReadOnlyList<TenantRecord>> GetAllAsync(CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            "catalog:tenants:all",
            async token =>
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(token);
                var tenants = await db.Tenants.AsNoTracking().ToListAsync(token);
                return (IReadOnlyList<TenantRecord>)tenants
                    .Select(t => new TenantRecord(t.Id, t.Code, t.Name, t.DefaultCulture, t.Status, protector.Unprotect(t.ProtectedConnectionString)))
                    .ToList();
            },
            CacheOptions,
            Tags,
            cancellationToken);
}
