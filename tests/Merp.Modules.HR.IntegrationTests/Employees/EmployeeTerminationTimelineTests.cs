using System.Net;
using System.Net.Http.Json;
using Merp.Modules.HR.Contracts.Employees;
using Merp.Modules.HR.IntegrationTests.Organization;

namespace Merp.Modules.HR.IntegrationTests.Employees;

/// <summary>Termination on the fake clock; its own host, because the clock only moves forward.</summary>
public sealed class EmployeeTerminationTimelineTests(HrClockFactory factory) : IClassFixture<HrClockFactory>
{
    private const string Url = "/api/hr/employees";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Terminate_FreesThePositionAfterTheLastDay()
    {
        var admin = factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var id = await setup.HireAsync(admin, new DateOnly(2025, 6, 1));
        var employment = (await EmployeeSetup.LoadAsync(factory, id)).Employments[0].Id.Value;
        factory.Time.SetUtcNow(new DateTimeOffset(2026, 1, 20, 9, 0, 0, TimeSpan.Zero));

        (await admin.PostAsJsonAsync(
            $"{Url}/{id}/terminate",
            new { employmentId = employment, date = "2026-01-20", sgkExitCode = "04", rehireable = false, reason = "Performans" },
            Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await EmployeeSetup.FilledAsync(admin, setup.CompanyId, setup.PositionA)).ShouldBe(1);
        factory.Time.SetUtcNow(new DateTimeOffset(2026, 1, 21, 9, 0, 0, TimeSpan.Zero));
        (await EmployeeSetup.FilledAsync(admin, setup.CompanyId, setup.PositionA)).ShouldBe(0);

        var ended = (await EmployeeSetup.LoadAsync(factory, id)).Employments[0];
        (ended.TerminationDate, ended.SgkExitCode, ended.Rehireable).ShouldBe((new DateOnly(2026, 1, 20), "04", false));
        (await factory.OutboxAsync<EmployeeTerminated>()).ShouldContain(e => e.EmploymentId == employment && e.TerminationDate == new DateOnly(2026, 1, 20));
    }
}
