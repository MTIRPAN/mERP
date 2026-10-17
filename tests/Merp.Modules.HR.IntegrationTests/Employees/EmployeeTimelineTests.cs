using System.Net.Http.Json;
using Merp.Application.Abstractions.Messaging;
using Merp.Modules.HR.Application.Employees;
using Merp.Modules.HR.Contracts.Employees;
using Merp.Modules.HR.IntegrationTests.Organization;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.HR.IntegrationTests.Employees;

/// <summary>
/// Dated changes on the fake clock, which starts on 5 January 2026 at 09:00 UTC (12:00 in Istanbul): a change takes
/// effect on its own day (PLAN H2 acceptance).
/// </summary>
public sealed class EmployeeTimelineTests(HrClockFactory factory) : IClassFixture<HrClockFactory>
{
    private const string Url = "/api/hr/employees";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ChangeAssignment_InTheFuture_TakesEffectOnItsDay()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var id = await setup.HireAsync(admin, new DateOnly(2026, 1, 1));
        var employment = (await EmployeeSetup.LoadAsync(factory, id)).Employments[0].Id.Value;
        var announced = (await factory.OutboxAsync<EmployeeAssignmentChanged>()).Count;

        var assignment = await HrFactory.CreatedIdAsync(await admin.PostAsJsonAsync(
            $"{Url}/{id}/assignments",
            new { employmentId = employment, validFrom = "2026-01-12", reason = "Promotion", positionId = setup.PositionB },
            Ct));

        (await EmployeeSetup.FilledAsync(admin, setup.CompanyId, setup.PositionA)).ShouldBe(1);
        (await EmployeeSetup.FilledAsync(admin, setup.CompanyId, setup.PositionB)).ShouldBe(0);
        (await factory.OutboxAsync<EmployeeAssignmentChanged>()).Count.ShouldBe(announced);

        // 12 January, 00:30 in Istanbul
        factory.Time.SetUtcNow(new DateTimeOffset(2026, 1, 11, 21, 30, 0, TimeSpan.Zero));

        (await EmployeeSetup.FilledAsync(admin, setup.CompanyId, setup.PositionA)).ShouldBe(0);
        (await EmployeeSetup.FilledAsync(admin, setup.CompanyId, setup.PositionB)).ShouldBe(1);
        (await AnnounceAsync()).ShouldBeGreaterThanOrEqualTo(1);
        (await factory.OutboxAsync<EmployeeAssignmentChanged>()).ShouldContain(e =>
            e.AssignmentId == assignment && e.PositionId == setup.PositionB && e.OrgUnitId == setup.UnitId && e.Reason == "Promotion");

        // Announced once
        await AnnounceAsync();
        (await factory.OutboxAsync<EmployeeAssignmentChanged>()).Count(e => e.AssignmentId == assignment).ShouldBe(1);
    }

    [Fact]
    public async Task ChangeAssignment_ThatHasStarted_IsAnnouncedRightAway()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var id = await setup.HireAsync(admin, new DateOnly(2025, 12, 1));
        var employment = (await EmployeeSetup.LoadAsync(factory, id)).Employments[0].Id.Value;

        var assignment = await HrFactory.CreatedIdAsync(await admin.PostAsJsonAsync(
            $"{Url}/{id}/assignments",
            new { employmentId = employment, validFrom = "2025-12-15", reason = "Reorganization", positionId = setup.PositionB, managerPositionId = setup.PositionA },
            Ct));

        (await factory.OutboxAsync<EmployeeAssignmentChanged>()).ShouldContain(e => e.AssignmentId == assignment);
        var assignments = (await EmployeeSetup.LoadAsync(factory, id)).Employments[0].Assignments;
        assignments.Single(a => a.Id.Value == assignment).ManagerPositionId!.Value.Value.ShouldBe(setup.PositionA);
        assignments.Single(a => a.Id.Value != assignment).ValidTo.ShouldBe(new DateOnly(2025, 12, 14));

        var earlier = await admin.PostAsJsonAsync(
            $"{Url}/{id}/assignments",
            new { employmentId = employment, validFrom = "2025-12-10", reason = "Transfer", positionId = setup.PositionA },
            Ct);
        (await Organization.Problem.ReadAsync(earlier)).Errors!["validFrom"].ShouldBe(["Yeni görev, son görevin başlangıcından önce başlayamaz."]);
    }

    private Task<int> AnnounceAsync() =>
        factory.InTenantAsync(async services =>
            (await services.GetRequiredService<ISender>().Send(new AnnounceStartedAssignmentsCommand(), Ct)).Value);
}
