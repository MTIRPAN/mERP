using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Application.Calendars;
using Merp.Modules.Platform.Application.Notifications;
using Merp.Modules.Platform.Contracts.Calendars;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Platform.IntegrationTests.Organization;
using Merp.Testing.Web;
using Merp.Web.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.IntegrationTests.Calendars;

/// <summary>Public holidays and work schedules (PLAN P9, D33).</summary>
public sealed class CalendarEndpointsTests(CalendarFactory factory) : IClassFixture<CalendarFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SeededHolidays_FollowTheRules_AndReligiousYearsAreKnown()
    {
        var calendar = await TurkishCalendarAsync();

        var year2026 = (await factory.Reader().GetFromJsonAsync<HolidayYear>($"/api/platform/holiday-calendars/{calendar.Id}/holidays?year=2026", Ct))!;
        year2026.HasMovableHolidays.ShouldBeTrue();
        year2026.Items.Count.ShouldBe(17);
        var eve = year2026.Items.Single(h => h.Date == new DateOnly(2026, 10, 28));
        eve.Duration.ShouldBe(Domain.Calendars.HolidayDuration.Half);
        eve.StartsAt.ShouldBe(new TimeOnly(13, 0));
        eve.DisplayName.ShouldBe("Cumhuriyet Bayramı arifesi");

        (await factory.Reader().GetFromJsonAsync<HolidayYear>($"/api/platform/holiday-calendars/{calendar.Id}/holidays?year=2027", Ct))!.Items.Count.ShouldBe(16);
        (await factory.Reader().GetFromJsonAsync<HolidayYear>($"/api/platform/holiday-calendars/{calendar.Id}/holidays?year=2030", Ct))!.HasMovableHolidays.ShouldBeFalse();
    }

    /// <summary>izin-kurallari.md §8.1 S1–S7 with the seeded holidays and schedules.</summary>
    [Theory]
    [InlineData("S1", "STANDART", "2026-10-26", "2026-10-30", 3.5)]
    [InlineData("S2", "STANDART", "2026-05-25", "2026-05-29", 1.5)]
    [InlineData("S3", "ALTIGUN", "2027-03-08", "2027-03-13", 2.5)]
    [InlineData("S4", "STANDART", "2027-05-17", "2027-05-21", 2)]
    [InlineData("S5", "STANDART", "2026-12-31", "2027-01-01", 1)]
    [InlineData("S7", "STANDART", "2026-10-30", "2026-11-02", 2)]
    public async Task Days_OfTheSeededSchedules_CountAsTheRulesSay(string scenario, string schedule, string from, string to, double expected)
    {
        var id = (await SchedulesAsync()).Single(s => s.Code == schedule).Id;

        var days = (await factory.Reader().GetFromJsonAsync<List<WorkingDay>>($"/api/platform/work-schedules/{id}/days?from={from}&to={to}", Ct))!;

        days.Sum(d => d.LeaveDayValue).ShouldBe((decimal)expected, scenario);
    }

    [Fact]
    public async Task AddingAHoliday_ChangesTheDaysAtOnce()
    {
        var admin = factory.Administrator();
        var calendar = await TurkishCalendarAsync();
        var standard = (await SchedulesAsync()).Single(s => s.Code == "STANDART").Id;
        var url = $"/api/platform/work-schedules/{standard}/days?from=2029-11-02&to=2029-11-02";
        (await admin.GetFromJsonAsync<List<WorkingDay>>(url, Ct))!.Single().LeaveDayValue.ShouldBe(1m);

        var add = await admin.PostAsJsonAsync($"/api/platform/holiday-calendars/{calendar.Id}/holidays", new
        {
            date = "2029-11-02",
            name = new Dictionary<string, string> { ["tr-TR"] = "Şirket tatili" },
            duration = "Full",
            isMovable = false,
        }, Ct);

        add.StatusCode.ShouldBe(HttpStatusCode.Created, await add.Content.ReadAsStringAsync(Ct));
        var day = (await admin.GetFromJsonAsync<List<WorkingDay>>(url, Ct))!.Single();
        day.LeaveDayValue.ShouldBe(0m);
        day.HolidayName.ShouldBe("Şirket tatili");

        var duplicate = await admin.PostAsJsonAsync($"/api/platform/holiday-calendars/{calendar.Id}/holidays", new
        {
            date = "2029-11-02",
            name = new Dictionary<string, string> { ["tr-TR"] = "Yine" },
            duration = "Full",
        }, Ct);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(duplicate)).Errors!["date"].ShouldBe(["Bu tarihte zaten bir resmi tatil var."]);
    }

    [Fact]
    public async Task CopyFixedHolidays_FillsTheNextYear_ButNotItsBayrams()
    {
        var calendar = await TurkishCalendarAsync();

        var copy = await factory.Administrator().PostAsJsonAsync($"/api/platform/holiday-calendars/{calendar.Id}/copy-fixed-holidays", new { year = 2028 }, Ct);

        copy.StatusCode.ShouldBe(HttpStatusCode.OK, await copy.Content.ReadAsStringAsync(Ct));
        (await copy.Content.ReadFromJsonAsync<CopiedHolidays>(Ct))!.Count.ShouldBe(8);
        var year = (await factory.Reader().GetFromJsonAsync<HolidayYear>($"/api/platform/holiday-calendars/{calendar.Id}/holidays?year=2028", Ct))!;
        year.Items.ShouldContain(h => h.Date == new DateOnly(2028, 5, 19));
        year.HasMovableHolidays.ShouldBeFalse();
    }

    [Fact]
    public async Task Reminder_NotifiesTheAdministrators_WhenNextYearsBayramsAreMissing()
    {
        // The administrator exists once they have signed in
        (await factory.Administrator().GetAsync("/api/platform/me", Ct)).EnsureSuccessStatusCode();

        (await factory.RemindAsync(2027)).ShouldBe(0);
        (await factory.RemindAsync(2031)).ShouldBe(1);

        var payloads = await factory.QueryAsync(db => db.OutboxMessages.AsNoTracking()
            .Where(m => m.Type == typeof(NotificationRequested).FullName)
            .Select(m => m.Payload)
            .ToListAsync(Ct));
        var reminder = payloads
            .Select(p => JsonSerializer.Deserialize<NotificationRequested>(p, JsonSerializerOptions.Web)!)
            .Where(n => n.Type == PlatformNotificationTypes.HolidaysMissing && n.Args["year"] == "2031")
            .ShouldHaveSingleItem();
        reminder.RecipientUserIds.ShouldNotBeEmpty();
        reminder.Args["calendar"].ShouldBe("TR");
        reminder.Link!.ShouldStartWith("/system/holidays?");
    }

    [Fact]
    public async Task Schedules_ResolveBranchThenCompanyThenTenant()
    {
        var admin = factory.Administrator();
        var standard = (await SchedulesAsync()).Single(s => s.Code == "STANDART");
        var companyId = await OrganizationFactory.CreateCompanyAsync(admin);
        var branchId = await OrganizationFactory.CreateBranchAsync(admin, companyId);
        (await factory.ResolveAsync(companyId, branchId)).ShouldBe(standard.Id);

        var companyDefault = await CreateScheduleAsync(admin, "S" + Guid.NewGuid().ToString("N")[..8], companyId, isDefault: true);
        (await factory.ResolveAsync(companyId, branchId)).ShouldBe(companyDefault);
        // The company's default does not replace the tenant's
        (await SchedulesAsync()).Single(s => s.Id == standard.Id).IsDefault.ShouldBeTrue();

        var branchSchedule = await CreateScheduleAsync(admin, "S" + Guid.NewGuid().ToString("N")[..8], companyId, isDefault: false);
        var branch = (await admin.GetFromJsonAsync<JsonElement>($"/api/platform/branches/{branchId}", Ct));
        var update = await admin.PutAsJsonAsync($"/api/platform/branches/{branchId}", new
        {
            version = branch.GetProperty("version").GetUInt32(),
            name = "Gebze Fabrika",
            type = "Factory",
            address = new { line1 = "OSB 3. Cad. No: 5", provinceCode = 41, districtCode = 1338 },
            isActive = true,
            workScheduleId = branchSchedule,
        }, Ct);
        update.StatusCode.ShouldBe(HttpStatusCode.NoContent, await update.Content.ReadAsStringAsync(Ct));
        (await factory.ResolveAsync(companyId, branchId)).ShouldBe(branchSchedule);
        (await admin.GetFromJsonAsync<JsonElement>($"/api/platform/branches/{branchId}", Ct)).GetProperty("workScheduleId").GetGuid().ShouldBe(branchSchedule);
    }

    [Fact]
    public async Task Branch_CannotUseAnotherCompanysSchedule()
    {
        var admin = factory.Administrator();
        var companyId = await OrganizationFactory.CreateCompanyAsync(admin);
        var otherCompany = await OrganizationFactory.CreateCompanyAsync(admin);
        var foreign = await CreateScheduleAsync(admin, "S" + Guid.NewGuid().ToString("N")[..8], otherCompany, isDefault: false);

        var response = await admin.PostAsJsonAsync("/api/platform/branches", new
        {
            companyId,
            code = OrganizationFactory.NewCode("B"),
            name = "Depo",
            type = "Warehouse",
            address = new { line1 = "OSB 3. Cad. No: 5", provinceCode = 41, districtCode = 1338 },
            workScheduleId = foreign,
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(response)).Errors!["workScheduleId"].ShouldBe(["Seçilen çalışma takvimi bu şube için kullanılamaz."]);
    }

    [Fact]
    public async Task NewTenantDefault_ReplacesTheOld_AndTheDefaultCannotBeDeactivated()
    {
        var admin = factory.Administrator();
        var standard = (await SchedulesAsync()).Single(s => s.Code == "STANDART");
        var replacement = await CreateScheduleAsync(admin, "S" + Guid.NewGuid().ToString("N")[..8], null, isDefault: true);
        try
        {
            var schedules = await SchedulesAsync();
            schedules.Single(s => s.Id == replacement).IsDefault.ShouldBeTrue();
            schedules.Single(s => s.Id == standard.Id).IsDefault.ShouldBeFalse();

            var current = schedules.Single(s => s.Id == replacement);
            var deactivate = await admin.PutAsJsonAsync($"/api/platform/work-schedules/{replacement}", UpdateBody(current, isDefault: true, isActive: false), Ct);
            deactivate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await Problem.ReadAsync(deactivate)).Code.ShouldBe("platform.calendars.errors.default_schedule_inactive");
        }
        finally
        {
            // Give the tenant its seeded default back for the other tests
            var latest = (await SchedulesAsync()).Single(s => s.Id == standard.Id);
            (await admin.PutAsJsonAsync($"/api/platform/work-schedules/{standard.Id}", UpdateBody(latest, isDefault: true, isActive: true), Ct)).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Schedule_WithAnInvalidDay_Returns400ForThatDay()
    {
        var calendar = await TurkishCalendarAsync();
        var days = Days(saturday: false);
        days[2] = new WorkDayDto(true, new TimeOnly(18, 0), new TimeOnly(8, 30), null, null);

        var response = await factory.Administrator().PostAsJsonAsync("/api/platform/work-schedules", new
        {
            code = "BOZUK",
            name = new Dictionary<string, string> { ["tr-TR"] = "Bozuk" },
            holidayCalendarId = calendar.Id,
            days,
            isDefault = false,
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(response)).Errors!.Keys.ShouldBe(["days[2]"]);
    }

    [Fact]
    public async Task ReadersCannotChange_AndCompanyScopedAdministratorsNeither()
    {
        var calendar = await TurkishCalendarAsync();
        var holiday = new { date = "2026-12-31", name = new Dictionary<string, string> { ["tr-TR"] = "Yılsonu" }, duration = "Half" };

        (await factory.Reader().PostAsJsonAsync($"/api/platform/holiday-calendars/{calendar.Id}/holidays", holiday, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await factory.CreateClientFor("nobody", "acme").GetAsync("/api/platform/work-schedules", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var companyId = await OrganizationFactory.CreateCompanyAsync(factory.Administrator());
        var scoped = factory.Administrator().WithScopes(AccessScope.Company(companyId));
        var response = await scoped.PostAsJsonAsync($"/api/platform/holiday-calendars/{calendar.Id}/holidays", holiday, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Problem.ReadAsync(response)).Code.ShouldBe("platform.calendars.errors.tenant_scope_required");
    }

    [Fact]
    public async Task UnknownCalendarOrSchedule_IsNotFound()
    {
        (await factory.Reader().GetAsync($"/api/platform/holiday-calendars/{Guid.NewGuid()}/holidays?year=2026", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await factory.Reader().GetAsync($"/api/platform/work-schedules/{Guid.NewGuid()}/days?from=2026-01-01&to=2026-01-31", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<HolidayCalendarItem> TurkishCalendarAsync() =>
        (await factory.Reader().GetFromJsonAsync<List<HolidayCalendarItem>>("/api/platform/holiday-calendars", Ct))!.Single(c => c.Code == "TR");

    private async Task<List<WorkScheduleItem>> SchedulesAsync() =>
        (await factory.Reader().GetFromJsonAsync<List<WorkScheduleItem>>("/api/platform/work-schedules", Ct))!;

    private async Task<Guid> CreateScheduleAsync(HttpClient admin, string code, Guid? companyId, bool isDefault)
    {
        var calendar = await TurkishCalendarAsync();
        var response = await admin.PostAsJsonAsync("/api/platform/work-schedules", new
        {
            code,
            name = new Dictionary<string, string> { ["tr-TR"] = code },
            companyId,
            holidayCalendarId = calendar.Id,
            days = Days(saturday: true),
            isDefault,
        }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<EntityCreatedResponse>(Ct))!.Id;
    }

    private static object UpdateBody(WorkScheduleItem schedule, bool isDefault, bool isActive) =>
        new { version = schedule.Version, name = schedule.Name, holidayCalendarId = schedule.HolidayCalendarId, days = schedule.Days, isDefault, isActive };

    private static WorkDayDto[] Days(bool saturday)
    {
        var weekday = new WorkDayDto(true, new TimeOnly(8, 30), new TimeOnly(18, 0), new TimeOnly(12, 30), new TimeOnly(13, 30));
        return
        [
            weekday, weekday, weekday, weekday, weekday,
            saturday ? new WorkDayDto(true, new TimeOnly(8, 30), new TimeOnly(13, 30), null, null) : new WorkDayDto(false, null, null, null, null),
            new WorkDayDto(false, null, null, null, null),
        ];
    }
}
