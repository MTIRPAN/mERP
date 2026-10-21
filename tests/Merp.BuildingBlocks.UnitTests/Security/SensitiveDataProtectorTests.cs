using Merp.Application.Abstractions.Context;
using Merp.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using NSubstitute;

namespace Merp.BuildingBlocks.UnitTests.Security;

public class SensitiveDataProtectorTests
{
    private readonly EphemeralDataProtectionProvider _keys = new();

    [Fact]
    public void Protect_EncryptsAndUnprotect_RestoresTheText()
    {
        var protector = For(Guid.CreateVersion7());

        var encrypted = protector.Protect(" 0 Rh+ ");

        encrypted.ShouldNotBeNull();
        encrypted.ShouldNotContain("Rh+");
        protector.Unprotect(encrypted).ShouldBe("0 Rh+");
    }

    [Fact]
    public void BlankValues_StayNull()
    {
        var protector = For(Guid.CreateVersion7());

        protector.Protect(null).ShouldBeNull();
        protector.Protect("  ").ShouldBeNull();
        protector.Unprotect(null).ShouldBeNull();
    }

    [Fact]
    public void AnotherTenant_CannotDecrypt()
    {
        var encrypted = For(Guid.CreateVersion7()).Protect("Sağlık notu");

        Should.Throw<System.Security.Cryptography.CryptographicException>(() => For(Guid.CreateVersion7()).Unprotect(encrypted));
    }

    private SensitiveDataProtector For(Guid tenantId)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.Tenant.Returns(new TenantInfo(tenantId, "acme", "Acme", "tr-TR"));
        tenant.RequiredTenant.Returns(new TenantInfo(tenantId, "acme", "Acme", "tr-TR"));
        return new SensitiveDataProtector(_keys, tenant);
    }
}
