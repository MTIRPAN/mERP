using System.Net;
using System.Net.Http.Json;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Leave;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.HR.IntegrationTests.Employees;
using Merp.Modules.HR.IntegrationTests.Organization;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.IntegrationTests.Leave;

/// <summary>
/// The leave request form filled with AI from the employee's words (PLAN A11, D64): only their own leave types and real
/// dates reach the form, their name and contacts never leave, nothing is saved. The fake clock stands on Monday
/// 5 January 2026.
/// </summary>
public sealed class LeaveRequestDraftTests : IClassFixture<HrClockFactory>
{
    private const string Url = "/api/hr/me/leave-requests/draft";

    private readonly HrClockFactory _factory;

    public LeaveRequestDraftTests(HrClockFactory factory)
    {
        _factory = factory;
        _factory.Ai.Reset();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheText_FillsTheForm_WithTheUsersOwnTypes_AndNothingIsSaved()
    {
        var person = await LinkedAsync();
        var employee = Self(person.Subject);
        var annual = (await employee.GetFromJsonAsync<List<LeaveTypeOption>>("/api/hr/me/leave-types", Ct))!.Single(t => t.Code == "YILLIK");
        _factory.Ai.Returns(new LeaveRequestDraftResult("yillik", "2026-01-09", "2026-01-12", LeaveDayPart.Full, LeaveDayPart.Full, "10:00", string.Empty, string.Empty, "Aile ziyareti", ["Yol izni ekleyecek misiniz?"]));

        var response = await employee.PostAsJsonAsync(Url, new { text = "Gelecek cuma ve pazartesi yıllık izin. Ayşe Yılmaz, 0532 111 22 33" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var draft = (await response.Content.ReadFromJsonAsync<LeaveRequestDraft>(Ct))!;
        draft.ShouldSatisfyAllConditions(
            d => d.LeaveTypeId.ShouldBe(annual.Id),
            d => d.StartDate.ShouldBe(new DateOnly(2026, 1, 9)),
            d => d.EndDate.ShouldBe(new DateOnly(2026, 1, 12)),
            d => d.StartTime.ShouldBeNull(),
            d => d.Note.ShouldBe("Aile ziyareti"),
            d => d.Questions.ShouldBe(["Yol izni ekleyecek misiniz?"]),
            d => d.PromptVersion.ShouldBe(LeaveRequestDraftPrompt.Version));
        var sent = _factory.Ai.Requests.ShouldHaveSingleItem();
        sent.Feature.ShouldBe(HrAiFeatures.LeaveRequestDraft);
        sent.Instructions.ShouldBe(LeaveRequestDraftPrompt.Instructions);
        sent.Input.ShouldSatisfyAllConditions(
            i => i.ShouldContain("Today: 2026-01-05 Monday"),
            i => i.ShouldContain("2026-01-09 Friday"),
            i => i.ShouldContain("<type code=\"YILLIK\""),
            i => i.ShouldContain("Gelecek cuma ve pazartesi yıllık izin."),
            i => i.ShouldNotContain("Ayşe"),
            i => i.ShouldNotContain("Yılmaz"),
            i => i.ShouldNotContain("0532"));
        (await _factory.QueryAsync(db => db.LeaveRequests.CountAsync(r => r.EmployeeId == person.EmployeeId, Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task Requests_AreChecked_AndOnlyForTheUsersOwnEmployments()
    {
        var person = await LinkedAsync();
        var other = await LinkedAsync();
        var employee = Self(person.Subject);

        (await Problem.ReadAsync(await employee.PostAsJsonAsync(Url, new { text = "" }, Ct))).Errors!.ShouldContainKey("text");
        (await employee.PostAsJsonAsync(Url, new { text = new string('a', DraftMyLeaveRequestQuery.TextMaxLength + 1) }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await employee.PostAsJsonAsync(Url, new { employmentId = other.EmploymentId, text = "Yarın izin" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _factory.CreateClientFor(person.Subject, HrFactory.Tenant, HrPermissions.Self.LeaveRead).PostAsJsonAsync(Url, new { text = "Yarın izin" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        _factory.Ai.IsConfigured = false;
        var disabled = await employee.PostAsJsonAsync(Url, new { text = "Yarın izin" }, Ct);
        disabled.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Problem.ReadAsync(disabled)).Code.ShouldBe("common.errors.ai_disabled");
        _factory.Ai.Requests.ShouldBeEmpty();
    }

    private sealed record Person(EmployeeId EmployeeId, Guid EmploymentId, string Subject);

    /// <summary>An employee with a user of their own.</summary>
    private async Task<Person> LinkedAsync()
    {
        var admin = _factory.Admin();
        var setup = await EmployeeSetup.CreateAsync(admin);
        var employee = await setup.HireAsync(admin, new DateOnly(2025, 12, 1));
        var email = $"personel.{Guid.NewGuid().ToString("N")[..10]}@acme.test";
        (await admin.PostAsJsonAsync($"/api/hr/employees/{employee}/user", new { email }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await _factory.DeliverMessagesAsync();
        var subject = _factory.Identity.Identities.Single(i => i.Value.Email == email).Key;
        var employmentId = (await EmployeeSetup.LoadAsync(_factory, employee)).Employments.Single().Id.Value;
        return new Person(EmployeeId.From(employee), employmentId, subject);
    }

    private HttpClient Self(string subject) =>
        _factory.CreateClientFor(subject, HrFactory.Tenant, HrPermissions.Self.LeaveRead, HrPermissions.Self.LeaveRequest);
}
