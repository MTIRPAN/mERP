using Merp.Infrastructure.MultiTenancy.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Merp.Infrastructure.MultiTenancy;

/// <summary>Catalog maintenance: migrations and tenant registration (development seed, migrator, tests).</summary>
public sealed class TenantCatalog(
    IDbContextFactory<CatalogDbContext> dbContextFactory,
    TenantConnectionStringProtector protector,
    ITenantStore tenantStore,
    TimeProvider timeProvider)
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>Registers the tenant if its code is not in the catalog yet. Returns the tenant id.</summary>
    public async Task<Guid> EnsureTenantAsync(string code, string name, string connectionString, string defaultCulture, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var normalized = Tenant.NormalizeCode(code);
        var existing = await db.Tenants.SingleOrDefaultAsync(t => t.Code == normalized, cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var tenant = Tenant.Create(code, name, protector.Protect(connectionString), defaultCulture, timeProvider.GetUtcNow());
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(cancellationToken);
        await tenantStore.InvalidateAsync(cancellationToken);
        return tenant.Id;
    }

    public async Task SetStatusAsync(string code, TenantStatus status, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var normalized = Tenant.NormalizeCode(code);
        var tenant = await db.Tenants.SingleAsync(t => t.Code == normalized, cancellationToken);
        if (status == TenantStatus.Active)
        {
            tenant.Activate();
        }
        else
        {
            tenant.Deactivate();
        }

        await db.SaveChangesAsync(cancellationToken);
        await tenantStore.InvalidateAsync(cancellationToken);
    }
}
