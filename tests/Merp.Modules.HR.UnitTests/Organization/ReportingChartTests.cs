using Merp.Modules.HR.Domain.Organization;

namespace Merp.Modules.HR.UnitTests.Organization;

/// <summary>
/// A three level company: the general manager's office on top, the sales directorate below it and two sales teams
/// below that (PLAN H1 acceptance: a three level structure).
/// </summary>
public class ReportingChartTests
{
    private static readonly Guid Company = Guid.CreateVersion7();

    private readonly OrgChartUnit _office = new(NewUnit(), null, null);
    private readonly PositionId _generalManager = NewPosition();
    private readonly PositionId _salesDirector = NewPosition();
    private readonly PositionId _istanbulLead = NewPosition();
    private readonly PositionId _istanbulRep = NewPosition();
    private readonly PositionId _ankaraRep = NewPosition();
    private readonly PositionId _assistant = NewPosition();

    [Fact]
    public void ManagerOf_FollowsTheUnitManagers_UpTheTree()
    {
        var chart = Chart();

        chart.ManagerOf(_istanbulRep).ShouldBe(_istanbulLead);
        chart.ManagerOf(_istanbulLead).ShouldBe(_salesDirector);

        // The Ankara team has no manager: the sales directorate's manager is the nearest one
        chart.ManagerOf(_ankaraRep).ShouldBe(_salesDirector);
        chart.ManagerOf(_salesDirector).ShouldBe(_generalManager);
        chart.ManagerOf(_generalManager).ShouldBeNull();
    }

    [Fact]
    public void ManagerOf_PrefersThePositionItReportsTo()
    {
        var chart = Chart(assistantReportsTo: _generalManager);

        chart.ManagerOf(_assistant).ShouldBe(_generalManager);
        chart.ChainOf(_assistant).ShouldBe([_generalManager]);
    }

    [Fact]
    public void ChainOf_ListsTheManagers_NearestFirst()
    {
        var chart = Chart();

        chart.ChainOf(_istanbulRep).ShouldBe([_istanbulLead, _salesDirector, _generalManager]);
        chart.ChainOf(_generalManager).ShouldBeEmpty();
    }

    [Fact]
    public void ClosedPositions_ArePassedThrough()
    {
        var chart = Chart(closed: _istanbulLead);

        chart.ManagerOf(_istanbulRep).ShouldBe(_salesDirector);
        chart.ChainOf(_istanbulRep).ShouldBe([_salesDirector, _generalManager]);
        chart.BuildLines(Company).ShouldNotContain(l => l.SubordinatePositionId == _istanbulLead || l.ManagerPositionId == _istanbulLead);
    }

    [Fact]
    public void BuildLines_HasOneRowPerPositionAndManagerAbove()
    {
        var chart = Chart();

        var lines = chart.BuildLines(Company);

        lines.ShouldAllBe(l => l.CompanyId == Company);
        lines.Where(l => l.SubordinatePositionId == _istanbulRep)
            .OrderBy(l => l.Depth)
            .Select(l => (l.ManagerPositionId, l.Depth))
            .ShouldBe([(_istanbulLead, 1), (_salesDirector, 2), (_generalManager, 3)]);
        lines.Count(l => l.ManagerPositionId == _generalManager).ShouldBe(5);
        lines.Count.ShouldBe(3 + 2 + 2 + 2 + 1);
    }

    [Fact]
    public void FindCycle_ReportsAPositionThatManagesItself()
    {
        // The sales director reports to a rep of the Istanbul team, who reports to the team lead, who reports to the director
        var chart = Chart(directorReportsTo: _istanbulRep);

        chart.FindCycle().ShouldNotBeNull();
        chart.ChainOf(_istanbulRep).ShouldBe([_istanbulLead, _salesDirector]);
        Chart().FindCycle().ShouldBeNull();
    }

    [Fact]
    public void ManagerOf_AnUnknownPosition_IsNull() =>
        Chart().ManagerOf(NewPosition()).ShouldBeNull();

    internal static OrgUnitId NewUnit() => OrgUnitId.From(Guid.CreateVersion7());

    internal static PositionId NewPosition() => PositionId.From(Guid.CreateVersion7());

    private ReportingChart Chart(
        PositionId? closed = null,
        PositionId? assistantReportsTo = null,
        PositionId? directorReportsTo = null)
    {
        var office = _office with { ManagerPositionId = _generalManager };
        var sales = new OrgChartUnit(NewUnit(), office.Id, _salesDirector);
        var istanbul = new OrgChartUnit(NewUnit(), sales.Id, _istanbulLead);
        var ankara = new OrgChartUnit(NewUnit(), sales.Id, null);

        OrgChartPosition Position(PositionId id, OrgChartUnit unit, PositionId? reportsTo = null) =>
            new(id, unit.Id, reportsTo, id == closed ? PositionStatus.Closed : PositionStatus.Active);

        return new ReportingChart(
            [office, sales, istanbul, ankara],
            [
                Position(_generalManager, office),
                Position(_assistant, sales, assistantReportsTo),
                Position(_salesDirector, sales, directorReportsTo),
                Position(_istanbulLead, istanbul),
                Position(_istanbulRep, istanbul),
                Position(_ankaraRep, ankara),
            ]);
    }
}
