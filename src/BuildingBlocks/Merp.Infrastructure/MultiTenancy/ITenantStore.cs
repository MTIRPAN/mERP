namespace Merp.Infrastructure.MultiTenancy;

/// <summary>Read access to the tenant catalog.</summary>
public interface ITenantStore
{
    Task<TenantRecord?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<TenantRecord?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantRecord>> GetActiveTenantsAsync(CancellationToken cancellationToken = default);

    /// <summary>Drops cached tenant data after a catalog change.</summary>
    Task InvalidateAsync(CancellationToken cancellationToken = default);
}
