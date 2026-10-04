using Merp.Application.Abstractions.Context;
using Merp.Infrastructure.MultiTenancy.Catalog;

namespace Merp.Infrastructure.MultiTenancy;

/// <summary>Cached, decrypted view of a catalog tenant.</summary>
public sealed record TenantRecord(Guid Id, string Code, string Name, string DefaultCulture, TenantStatus Status, string ConnectionString)
{
    public bool IsActive => Status == TenantStatus.Active;

    public TenantInfo ToTenantInfo() => new(Id, Code, Name, DefaultCulture);
}
