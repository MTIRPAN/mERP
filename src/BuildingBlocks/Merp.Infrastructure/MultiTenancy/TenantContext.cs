using Merp.Application.Abstractions.Context;

namespace Merp.Infrastructure.MultiTenancy;

/// <summary>
/// Scoped holder of the resolved tenant and its database connection string (D3). Set once per request by the
/// tenant resolution middleware, or per job by the tenant job runner.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    private string? _connectionString;

    public TenantInfo? Tenant { get; private set; }

    /// <summary>Connection string of the tenant database; throws when no tenant has been resolved.</summary>
    public string ConnectionString =>
        _connectionString ?? throw new InvalidOperationException("No tenant has been resolved for this scope, so no tenant database is available.");

    public void Set(TenantInfo tenant, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        if (Tenant is not null && Tenant.Id != tenant.Id)
        {
            throw new InvalidOperationException($"The scope already belongs to tenant '{Tenant.Code}' and cannot switch to '{tenant.Code}'.");
        }

        Tenant = tenant;
        _connectionString = connectionString;
    }
}
