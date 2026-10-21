using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Security;
using Microsoft.AspNetCore.DataProtection;

namespace Merp.Infrastructure.Security;

/// <summary>
/// <see cref="ISensitiveDataProtector"/> with ASP.NET Data Protection (D13, D38). The purpose chain ends with the
/// tenant's id, so one tenant's values cannot be decrypted as another's. The key ring is the catalog's (D3).
/// </summary>
public sealed class SensitiveDataProtector(IDataProtectionProvider dataProtectionProvider, ITenantContext tenant) : ISensitiveDataProtector
{
    public const string Purpose = "Merp.SensitiveData";

    private IDataProtector Protector => dataProtectionProvider.CreateProtector(Purpose, tenant.RequiredTenant.Id.ToString("N"));

    public string? Protect(string? value) => string.IsNullOrWhiteSpace(value) ? null : Protector.Protect(value.Trim());

    public string? Unprotect(string? protectedValue) => protectedValue is null ? null : Protector.Unprotect(protectedValue);
}
