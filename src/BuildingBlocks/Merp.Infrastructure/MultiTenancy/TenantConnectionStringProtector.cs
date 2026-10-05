using Microsoft.AspNetCore.DataProtection;

namespace Merp.Infrastructure.MultiTenancy;

/// <summary>Encrypts tenant connection strings before they are stored in the catalog (D3).</summary>
public sealed class TenantConnectionStringProtector(IDataProtectionProvider dataProtectionProvider)
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("Merp.Catalog.TenantConnectionString");

    public string Protect(string connectionString) => _protector.Protect(connectionString);

    public string Unprotect(string protectedConnectionString) => _protector.Unprotect(protectedConnectionString);
}
