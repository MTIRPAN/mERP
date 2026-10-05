namespace Merp.Application.Abstractions.Context;

/// <summary>Tenant of the current request or job (D3).</summary>
public interface ITenantContext
{
    TenantInfo? Tenant { get; }

    bool HasTenant => Tenant is not null;

    /// <summary>The current tenant; throws when no tenant has been resolved.</summary>
    TenantInfo RequiredTenant => Tenant ?? throw new InvalidOperationException("No tenant has been resolved for this scope.");
}

public sealed record TenantInfo(Guid Id, string Code, string Name, string DefaultCulture);
