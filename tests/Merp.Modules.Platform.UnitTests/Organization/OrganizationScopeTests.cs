using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Application.Organization;
using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Domain.Companies;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Platform.UnitTests.Organization;

public class OrganizationScopeTests
{
    private static readonly CompanyId Acme = CompanyId.From(Guid.CreateVersion7());
    private static readonly CompanyId Globex = CompanyId.From(Guid.CreateVersion7());

    [Fact]
    public void TenantScope_CoversEverything()
    {
        var scope = OrganizationScope.From([AccessScope.Tenant]);

        scope.IsTenantWide.ShouldBeTrue();
        scope.CoversCompany(Acme).ShouldBeTrue();
        scope.CoversBranch(Branch.Create(Globex, Code.Create("B1").Value, BranchTests.Profile())).ShouldBeTrue();
    }

    [Fact]
    public void CompanyScope_CoversTheCompanyAndItsBranchesOnly()
    {
        var scope = OrganizationScope.From([AccessScope.Company(Acme.Value)]);

        scope.CoversCompany(Acme).ShouldBeTrue();
        scope.CoversCompany(Globex).ShouldBeFalse();
        scope.CoversBranch(Branch.Create(Acme, Code.Create("B1").Value, BranchTests.Profile())).ShouldBeTrue();
        scope.CoversBranch(Branch.Create(Globex, Code.Create("B1").Value, BranchTests.Profile())).ShouldBeFalse();
    }

    [Fact]
    public void BranchScope_CoversTheBranchButNotItsCompany()
    {
        var own = Branch.Create(Acme, Code.Create("B1").Value, BranchTests.Profile());
        var other = Branch.Create(Acme, Code.Create("B2").Value, BranchTests.Profile());
        var scope = OrganizationScope.From([AccessScope.Branch(own.Id.Value)]);

        scope.CoversBranch(own).ShouldBeTrue();
        scope.CoversBranch(other).ShouldBeFalse();
        scope.CoversCompany(Acme).ShouldBeFalse();
    }

    [Fact]
    public void SeveralAssignments_AreUnited()
    {
        var scope = OrganizationScope.From([AccessScope.Company(Acme.Value), AccessScope.Company(Globex.Value), AccessScope.Company(Acme.Value)]);

        scope.CompanyIds.ShouldBe([Acme, Globex], ignoreOrder: true);
        scope.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void ScopesOfOtherModules_DoNotReachCompaniesYet()
    {
        var scope = OrganizationScope.From([new AccessScope(AccessScopeTypes.Self), new AccessScope(AccessScopeTypes.OrgUnit, Guid.CreateVersion7())]);

        scope.IsEmpty.ShouldBeTrue();
        scope.CoversCompany(Acme).ShouldBeFalse();
    }

    [Fact]
    public void FilterBranches_KeepsTheScopedBranches()
    {
        var acme1 = Branch.Create(Acme, Code.Create("A1").Value, BranchTests.Profile());
        var globex1 = Branch.Create(Globex, Code.Create("G1").Value, BranchTests.Profile());
        var globex2 = Branch.Create(Globex, Code.Create("G2").Value, BranchTests.Profile());
        var scope = OrganizationScope.From([AccessScope.Company(Acme.Value), AccessScope.Branch(globex2.Id.Value)]);

        scope.FilterBranches(new[] { acme1, globex1, globex2 }.AsQueryable()).ShouldBe([acme1, globex2]);
    }
}
