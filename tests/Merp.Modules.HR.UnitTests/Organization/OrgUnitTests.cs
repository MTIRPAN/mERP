using Merp.Modules.HR.Domain.Organization;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.HR.UnitTests.Organization;

public class OrgUnitTests
{
    private static readonly Guid Company = Guid.CreateVersion7();

    internal static OrgUnitProfile Profile(string name = "Satış Direktörlüğü", OrgUnitType type = OrgUnitType.Directorate) =>
        new(name, type, "MM-100", null, null, null);

    internal static OrgUnit Unit(string code, OrgUnit? parent = null, Guid? company = null) =>
        OrgUnit.Create(company ?? Company, parent, Code.Create(code).Value, Profile(code));

    [Fact]
    public void Create_AtTheTop_HasItsOwnIdAsPath()
    {
        var unit = OrgUnit.Create(Company, null, Code.Create("satis").Value, Profile());

        unit.CompanyId.ShouldBe(Company);
        ((ICompanyScoped)unit).CompanyId.ShouldBe(Company);
        unit.Code.ShouldBe("SATIS");
        unit.Name.ShouldBe("Satış Direktörlüğü");
        unit.Type.ShouldBe(OrgUnitType.Directorate);
        unit.CostCenterCode.ShouldBe("MM-100");
        unit.ParentId.ShouldBeNull();
        unit.Path.ShouldBe(unit.Id.Value.ToString("N"));
        unit.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Create_UnderAParent_ExtendsTheParentsPath()
    {
        var top = Unit("SATIS");
        var region = Unit("MARMARA", top);
        var team = Unit("IST", region);

        region.ParentId.ShouldBe(top.Id);
        team.Path.ShouldBe($"{top.Path}.{region.Id.Value:N}.{team.Id.Value:N}");
        team.Path.Split('.').Length.ShouldBe(3);
    }

    [Fact]
    public void Create_UnderAParentOfAnotherCompany_Throws() =>
        Should.Throw<ArgumentException>(() => Unit("IST", Unit("SATIS", company: Guid.CreateVersion7())));

    [Fact]
    public void Create_WithoutCompany_OrWithAnEndBeforeTheStart_Throws()
    {
        Should.Throw<ArgumentException>(() => OrgUnit.Create(Guid.Empty, null, Code.Create("A").Value, Profile()));
        Should.Throw<ArgumentException>(() => OrgUnit.Create(
            Company,
            null,
            Code.Create("A").Value,
            Profile() with { ValidFrom = new DateOnly(2026, 2, 1), ValidTo = new DateOnly(2026, 1, 31) }));
    }

    [Fact]
    public void MoveTo_AnotherParent_RepointsThePath_AndRaisesTheEvents()
    {
        var sales = Unit("SATIS");
        var marketing = Unit("PAZARLAMA");
        var region = Unit("MARMARA", sales);
        var oldPath = region.Path;

        region.MoveTo(marketing).IsSuccess.ShouldBeTrue();

        region.ParentId.ShouldBe(marketing.Id);
        region.Path.ShouldBe($"{marketing.Path}.{region.Id.Value:N}");
        region.DomainEvents.ShouldBe(
        [
            new OrgUnitMoved(region.Id, Company, oldPath, region.Path),
            new ReportingStructureChanged(Company),
        ]);
    }

    [Fact]
    public void MoveTo_TheTop_MakesItARootUnit()
    {
        var sales = Unit("SATIS");
        var region = Unit("MARMARA", sales);

        region.MoveTo(null).IsSuccess.ShouldBeTrue();

        region.ParentId.ShouldBeNull();
        region.Path.ShouldBe(region.Id.Value.ToString("N"));
    }

    [Fact]
    public void MoveTo_ItselfOrASubUnit_IsRefused()
    {
        var sales = Unit("SATIS");
        var region = Unit("MARMARA", sales);
        var team = Unit("IST", region);

        sales.MoveTo(sales).Error.Fields.ShouldHaveSingleItem().Code.ShouldBe("hr.org_units.errors.move_into_own_subtree");
        sales.MoveTo(team).Error.ShouldBe(OrgUnitErrors.MoveIntoOwnSubtree);
        sales.ParentId.ShouldBeNull();
        sales.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void MoveTo_AUnitOfAnotherCompany_IsRefused()
    {
        var region = Unit("MARMARA", Unit("SATIS"));

        region.MoveTo(Unit("SATIS", company: Guid.CreateVersion7())).Error.ShouldBe(OrgUnitErrors.ParentInOtherCompany);
    }

    [Fact]
    public void MoveTo_TheSameParent_ChangesNothing()
    {
        var sales = Unit("SATIS");
        var region = Unit("MARMARA", sales);

        region.MoveTo(sales).IsSuccess.ShouldBeTrue();

        region.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void ChangeManager_RaisesTheEvent_OnlyWhenTheManagerChanges()
    {
        var unit = Unit("SATIS");
        var manager = PositionId.From(Guid.CreateVersion7());

        unit.ChangeManager(manager);
        unit.ChangeManager(manager);

        unit.ManagerPositionId.ShouldBe(manager);
        unit.DomainEvents.ShouldBe([new ReportingStructureChanged(Company)]);
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("2026-01-01", null, true)]
    [InlineData("2026-03-02", null, false)]
    [InlineData(null, "2026-03-01", true)]
    [InlineData(null, "2026-02-28", false)]
    public void IsActiveOn_IncludesBothEnds(string? from, string? to, bool expected)
    {
        var unit = OrgUnit.Create(Company, null, Code.Create("A").Value, Profile() with
        {
            ValidFrom = from is null ? null : DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture),
            ValidTo = to is null ? null : DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture),
        });

        unit.IsActiveOn(new DateOnly(2026, 3, 1)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("a", "a", true)]
    [InlineData("a.b", "a", true)]
    [InlineData("a.b.c", "a.b", true)]
    [InlineData("ab", "a", false)]
    [InlineData("a", "a.b", false)]
    public void IsWithin_MatchesWholeLabels(string path, string ancestor, bool expected) =>
        OrgPaths.IsWithin(path, ancestor).ShouldBe(expected);
}
