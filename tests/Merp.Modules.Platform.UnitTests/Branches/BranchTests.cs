using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Domain.Companies;
using Merp.Modules.Platform.UnitTests.Companies;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;

namespace Merp.Modules.Platform.UnitTests.Branches;

public sealed class BranchTests
{
    private static readonly CompanyId Company = EntityId.New<CompanyId>();

    [Fact]
    public void Create_BelongsToTheCompany_WithNormalizedCode()
    {
        var branch = Branch.Create(Company, " ist-01 ", new BranchDetails(" Kadıköy Mağazası ", BranchType.Store, CompanyTests.IstanbulKadikoy(), null));

        branch.CompanyId.ShouldBe(Company);
        ((ICompanyScoped)branch).CompanyId.ShouldBe(Company.Value);
        branch.Code.ShouldBe("IST-01");
        branch.Name.ShouldBe("Kadıköy Mağazası");
        branch.Type.ShouldBe(BranchType.Store);
        branch.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Update_ChangesDetailsAndActiveState_ButNotCompanyOrCode()
    {
        var branch = Branch.Create(Company, "IST-01", new BranchDetails("Kadıköy", BranchType.Store, CompanyTests.IstanbulKadikoy(), null));

        branch.Update(new BranchDetails("Kadıköy Depo", BranchType.Warehouse, CompanyTests.IstanbulKadikoy(), null), isActive: false);

        branch.CompanyId.ShouldBe(Company);
        branch.Code.ShouldBe("IST-01");
        branch.Name.ShouldBe("Kadıköy Depo");
        branch.Type.ShouldBe(BranchType.Warehouse);
        branch.IsActive.ShouldBeFalse();
    }
}
