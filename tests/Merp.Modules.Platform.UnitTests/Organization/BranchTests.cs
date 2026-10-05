using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Domain.Companies;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Platform.UnitTests.Organization;

public class BranchTests
{
    private static readonly CompanyId Company = CompanyId.From(Guid.CreateVersion7());

    internal static BranchProfile Profile(string name = "Gebze Fabrika", BranchType type = BranchType.Factory) =>
        new(
            name,
            type,
            Address.Create("OSB 3. Cad. No: 5", null, "Gebze", "Kocaeli", null, "TR", 41, 1338).Value,
            SgkWorkplaceNumber.Create("21234010112345670341234000").Value,
            null);

    [Fact]
    public void Create_BelongsToTheCompany_AndIsActive()
    {
        var branch = Branch.Create(Company, Code.Create("gbz").Value, Profile());

        branch.CompanyId.ShouldBe(Company);
        ((ICompanyScoped)branch).CompanyId.ShouldBe(Company.Value);
        branch.Code.ShouldBe("GBZ");
        branch.Type.ShouldBe(BranchType.Factory);
        branch.Address.City.ShouldBe("Kocaeli");
        branch.SgkWorkplaceNumber.ShouldBe("21234010112345670341234000");
        branch.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Update_ChangesDetails_ButNotTheCompanyOrCode()
    {
        var branch = Branch.Create(Company, Code.Create("GBZ").Value, Profile());

        branch.Update(Profile("Gebze Depo", BranchType.Warehouse));
        branch.Deactivate();

        branch.CompanyId.ShouldBe(Company);
        branch.Code.ShouldBe("GBZ");
        branch.Name.ShouldBe("Gebze Depo");
        branch.Type.ShouldBe(BranchType.Warehouse);
        branch.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Create_WithoutCompany_Throws() =>
        Should.Throw<ArgumentException>(() => Branch.Create(default, Code.Create("GBZ").Value, Profile()));

    [Fact]
    public void Create_WithUnknownType_Throws() =>
        Should.Throw<ArgumentOutOfRangeException>(() => Branch.Create(Company, Code.Create("GBZ").Value, Profile(type: (BranchType)42)));

    [Fact]
    public void Create_WithoutAddress_Throws() =>
        Should.Throw<ArgumentNullException>(() => Branch.Create(Company, Code.Create("GBZ").Value, Profile() with { Address = null! }));
}
