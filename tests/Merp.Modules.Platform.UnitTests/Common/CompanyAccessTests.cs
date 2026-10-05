using Merp.Modules.Platform.Application.Common;
using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Domain.Companies;
using Merp.Modules.Platform.UnitTests.Companies;
using Merp.SharedKernel.Identity;

namespace Merp.Modules.Platform.UnitTests.Common;

public sealed class CompanyAccessTests
{
    private static readonly CompanyId Acme = EntityId.New<CompanyId>();
    private static readonly CompanyId Globex = EntityId.New<CompanyId>();
    private static readonly Branch AcmeStore = Branch.Create(Acme, "IST", new BranchDetails("İstanbul", BranchType.Store, CompanyTests.IstanbulKadikoy(), null));

    [Fact]
    public void TenantScope_CoversEverything()
    {
        var access = new CompanyAccess(All: true, [], []);

        access.CoversCompany(Globex).ShouldBeTrue();
        access.CoversBranch(AcmeStore).ShouldBeTrue();
    }

    [Fact]
    public void CompanyScope_CoversTheCompanyAndItsBranches()
    {
        var access = new CompanyAccess(All: false, [Acme], []);

        access.CoversCompany(Acme).ShouldBeTrue();
        access.CoversCompany(Globex).ShouldBeFalse();
        access.CoversBranch(AcmeStore).ShouldBeTrue();
    }

    [Fact]
    public void BranchScope_CoversTheBranchOnly()
    {
        var access = new CompanyAccess(All: false, [], [AcmeStore.Id]);

        access.CoversBranch(AcmeStore).ShouldBeTrue();
        access.CoversCompany(Acme).ShouldBeFalse();
    }

    [Fact]
    public void NoScope_CoversNothing()
    {
        var access = new CompanyAccess(All: false, [], []);

        access.CoversCompany(Acme).ShouldBeFalse();
        access.CoversBranch(AcmeStore).ShouldBeFalse();
    }
}
