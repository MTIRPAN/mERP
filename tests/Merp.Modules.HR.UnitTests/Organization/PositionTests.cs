using Merp.Modules.HR.Domain.Organization;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.HR.UnitTests.Organization;

public class PositionTests
{
    private static readonly Guid Company = Guid.CreateVersion7();
    private static readonly OrgUnitId Unit = OrgUnitId.From(Guid.CreateVersion7());
    private static readonly JobId Job = JobId.From(Guid.CreateVersion7());

    private static PositionProfile Profile(int norm = 1, PositionStatus status = PositionStatus.Active, PositionId? reportsTo = null) =>
        new(Unit, Job, reportsTo, norm, status, IsManager: false);

    [Fact]
    public void Create_IsActive_AndRebuildsTheReportingLines()
    {
        var position = Position.Create(Company, Code.Create("sat-01").Value, Profile(norm: 3));

        position.CompanyId.ShouldBe(Company);
        ((ICompanyScoped)position).CompanyId.ShouldBe(Company);
        position.Code.ShouldBe("SAT-01");
        position.Norm.ShouldBe(3);
        position.Status.ShouldBe(PositionStatus.Active);
        position.DomainEvents.ShouldBe([new ReportingStructureChanged(Company)]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public void Create_WithSeatsOutOfRange_Throws(int norm) =>
        Should.Throw<ArgumentOutOfRangeException>(() => Position.Create(Company, Code.Create("P").Value, Profile(norm)));

    [Fact]
    public void Update_RebuildsTheReportingLines_OnlyForStructuralChanges()
    {
        var position = Position.Create(Company, Code.Create("P").Value, Profile());
        position.ClearDomainEvents();

        position.Update(Profile(norm: 5) with { IsManager = true });
        position.DomainEvents.ShouldBeEmpty();

        position.Update(Profile(norm: 5, status: PositionStatus.Frozen));
        position.DomainEvents.ShouldBe([new ReportingStructureChanged(Company)]);
    }

    [Fact]
    public void Update_ReportingToItself_Throws()
    {
        var position = Position.Create(Company, Code.Create("P").Value, Profile());

        Should.Throw<ArgumentException>(() => position.Update(Profile(reportsTo: position.Id)));
    }

    [Theory]
    [InlineData(PositionStatus.Active, 3, 1, 2)]
    [InlineData(PositionStatus.Active, 2, 3, 0)]
    [InlineData(PositionStatus.Frozen, 3, 1, 0)]
    [InlineData(PositionStatus.Closed, 3, 0, 0)]
    public void VacantSeats_CountOnlyForActivePositions(PositionStatus status, int norm, int filled, int vacant)
    {
        var position = Position.Create(Company, Code.Create("P").Value, Profile(norm, status));

        position.VacantSeats(filled).ShouldBe(vacant);
    }
}
