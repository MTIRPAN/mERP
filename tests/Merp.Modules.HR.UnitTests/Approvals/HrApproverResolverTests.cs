using System.Text.Json;
using Merp.Modules.HR.Application.Approvals;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Domain.Organization;
using Merp.Modules.Platform.Contracts.Approvals;
using Microsoft.Extensions.Logging.Abstractions;

namespace Merp.Modules.HR.UnitTests.Approvals;

/// <summary>
/// The HR resolvers over a three level company with fake position holders (D37: the end to end test follows in H3,
/// when employees get users). General manager → sales director → Istanbul team lead → Istanbul rep.
/// </summary>
public class HrApproverResolverTests
{
    private static readonly Guid Company = Guid.CreateVersion7();
    private static readonly JsonElement NoFacts = JsonSerializer.SerializeToElement(new { });

    private readonly PositionId _generalManager = PositionId.From(Guid.CreateVersion7());
    private readonly PositionId _salesDirector = PositionId.From(Guid.CreateVersion7());
    private readonly PositionId _istanbulLead = PositionId.From(Guid.CreateVersion7());
    private readonly PositionId _istanbulRep = PositionId.From(Guid.CreateVersion7());
    private readonly Person _ayse = new(Guid.CreateVersion7(), Guid.CreateVersion7());
    private readonly Person _burak = new(Guid.CreateVersion7(), Guid.CreateVersion7());
    private readonly Person _cem = new(Guid.CreateVersion7(), Guid.CreateVersion7());
    private readonly Person _deniz = new(Guid.CreateVersion7(), Guid.CreateVersion7());
    private readonly FakeHolders _holders = new();
    private readonly ReportingChart _chart;

    public HrApproverResolverTests()
    {
        var office = new OrgChartUnit(OrgUnitId.From(Guid.CreateVersion7()), null, _generalManager);
        var sales = new OrgChartUnit(OrgUnitId.From(Guid.CreateVersion7()), office.Id, _salesDirector);
        var istanbul = new OrgChartUnit(OrgUnitId.From(Guid.CreateVersion7()), sales.Id, _istanbulLead);
        _chart = new ReportingChart(
            [office, sales, istanbul],
            [
                new OrgChartPosition(_generalManager, office.Id, null, PositionStatus.Active),
                new OrgChartPosition(_salesDirector, sales.Id, null, PositionStatus.Active),
                new OrgChartPosition(_istanbulLead, istanbul.Id, null, PositionStatus.Active),
                new OrgChartPosition(_istanbulRep, istanbul.Id, null, PositionStatus.Active),
            ]);

        _holders.Hold(_generalManager, _ayse);
        _holders.Hold(_salesDirector, _burak);
        _holders.Hold(_istanbulLead, _cem);
        _holders.Hold(_istanbulRep, _deniz);
    }

    [Fact]
    public async Task LineManager2_ReturnsTheManagersManager()
    {
        (await LineManager(2, _deniz)).ShouldBe([_burak.UserId!.Value]);
        (await LineManager(1, _deniz)).ShouldBe([_cem.UserId!.Value]);
        (await LineManager(3, _deniz)).ShouldBe([_ayse.UserId!.Value]);
        (await LineManager(4, _deniz)).ShouldBeEmpty();
    }

    [Fact]
    public async Task LineManager_SkipsVacantPositions()
    {
        _holders.Vacate(_istanbulLead);

        (await LineManager(1, _deniz)).ShouldBe([_burak.UserId!.Value]);
        (await LineManager(2, _deniz)).ShouldBe([_ayse.UserId!.Value]);
    }

    [Fact]
    public async Task LineManager_WhoseManagerHasNoUser_FindsNobody()
    {
        var withoutUser = new Person(Guid.CreateVersion7(), null);
        _holders.Vacate(_istanbulLead);
        _holders.Hold(_istanbulLead, withoutUser);

        (await LineManager(1, _deniz)).ShouldBeEmpty();
        (await LineManager(2, _deniz)).ShouldBe([_burak.UserId!.Value]);
    }

    [Fact]
    public async Task LineManager_FindsTheRequesterByUser_WhenTheEmployeeIsNotGiven()
    {
        var resolver = new LineManagerApproverResolver(_holders, new ChartLines(_chart), NullLogger<LineManagerApproverResolver>.Instance);

        var users = await resolver.ResolveAsync(
            new ApproverResolutionContext("hr.leave-request", Company, _deniz.UserId!.Value, null, NoFacts),
            new Dictionary<string, string> { [LineManagerApproverResolver.LevelParameter] = "1" },
            TestContext.Current.CancellationToken);

        users.ShouldBe([_cem.UserId!.Value]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("iki")]
    [InlineData(null)]
    public async Task LineManager_WithAnInvalidLevel_FindsNobody(string? level)
    {
        var resolver = new LineManagerApproverResolver(_holders, new ChartLines(_chart), NullLogger<LineManagerApproverResolver>.Instance);
        var parameters = level is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [LineManagerApproverResolver.LevelParameter] = level };

        (await resolver.ResolveAsync(Context(_deniz), parameters, TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task LineManager_ForARequesterWithoutPosition_FindsNobody() =>
        (await LineManager(1, new Person(Guid.CreateVersion7(), Guid.CreateVersion7()))).ShouldBeEmpty();

    [Fact]
    public async Task Position_ReturnsTheUsersOfItsHolders()
    {
        var resolver = new PositionApproverResolver(_holders);
        var parameters = new Dictionary<string, string> { [PositionApproverResolver.PositionIdParameter] = _salesDirector.Value.ToString() };

        (await resolver.ResolveAsync(Context(_deniz), parameters, TestContext.Current.CancellationToken)).ShouldBe([_burak.UserId!.Value]);
        (await resolver.ResolveAsync(Context(_deniz), new Dictionary<string, string>(), TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ManagerLookup_ReturnsTheNearestManagerWithAUser()
    {
        var lookup = new HrApproverManagerLookup(_holders, new ChartLines(_chart));

        (await lookup.FindManagerUserIdAsync(_cem.UserId!.Value, TestContext.Current.CancellationToken)).ShouldBe(_burak.UserId);
        (await lookup.FindManagerUserIdAsync(_ayse.UserId!.Value, TestContext.Current.CancellationToken)).ShouldBeNull();

        _holders.Vacate(_salesDirector);
        (await lookup.FindManagerUserIdAsync(_cem.UserId!.Value, TestContext.Current.CancellationToken)).ShouldBe(_ayse.UserId);
    }

    private static ApproverResolutionContext Context(Person requester) =>
        new("hr.leave-request", Company, requester.UserId ?? Guid.CreateVersion7(), requester.EmployeeId, NoFacts);

    private Task<IReadOnlyList<Guid>> LineManager(int level, Person requester) =>
        new LineManagerApproverResolver(_holders, new ChartLines(_chart), NullLogger<LineManagerApproverResolver>.Instance).ResolveAsync(
            Context(requester),
            new Dictionary<string, string> { [LineManagerApproverResolver.LevelParameter] = level.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            TestContext.Current.CancellationToken);

    private sealed record Person(Guid EmployeeId, Guid? UserId);

    /// <summary>Reporting lines as the closure table would hold them, from the chart.</summary>
    private sealed class ChartLines(ReportingChart chart) : IReportingLines
    {
        public Task<IReadOnlyList<PositionId>> GetManagerChainAsync(PositionId positionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(chart.ChainOf(positionId));
    }

    private sealed class FakeHolders : IPositionHolders
    {
        private readonly List<PositionHolder> _holders = [];

        public void Hold(PositionId position, Person person) => _holders.Add(new PositionHolder(position, person.EmployeeId, person.UserId, IsPrimary: true));

        public void Vacate(PositionId position) => _holders.RemoveAll(h => h.PositionId == position);

        public Task<IReadOnlyList<PositionHolder>> GetHoldersAsync(IReadOnlyCollection<PositionId> positionIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PositionHolder>>([.. _holders.Where(h => positionIds.Contains(h.PositionId))]);

        public Task<IReadOnlyList<PositionHolder>> GetPositionsOfEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PositionHolder>>([.. _holders.Where(h => h.EmployeeId == employeeId)]);

        public Task<IReadOnlyList<PositionHolder>> GetPositionsOfUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PositionHolder>>([.. _holders.Where(h => h.UserId == userId)]);
    }
}
