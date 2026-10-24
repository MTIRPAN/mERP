using System.Globalization;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Documents;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.HR.Application.Employees;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.Platform.Contracts.Documents;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Leave;

/// <summary>
/// The annual leave record of an employment as a PDF (yıllık izin kayıt belgesi, PLAN L6, D46): seniority, the days
/// earned and the leaves taken with their dates, the totals and the signature fields.
/// </summary>
public sealed record GetLeaveRecordDocumentQuery(Guid EmployeeId, Guid EmploymentId) : IQuery<HrFileDownload>, IRequirePermission
{
    public string Permission => HrPermissions.Leave.Balances.Read;
}

/// <summary>A leave request form as a PDF, to be signed (PLAN L6, D46); a request in the caller's scope.</summary>
public sealed record GetLeaveRequestFormQuery(Guid Id) : IQuery<HrFileDownload>, IRequirePermission
{
    public string Permission => HrPermissions.Leave.Requests.Read;
}

/// <summary>The form of one of the user's own leave requests as a PDF.</summary>
public sealed record GetMyLeaveRequestFormQuery(Guid Id) : IQuery<HrFileDownload>, IRequirePermission
{
    public string Permission => HrPermissions.Self.LeaveRead;
}

/// <summary>The documents HR prints (D36): the annual leave record and the leave request form.</summary>
public sealed class HrDocumentTypes : IDocumentTypeProvider
{
    public const string LeaveRecord = "hr.leave_record";
    public const string LeaveForm = "hr.leave_form";

    private const string Css = """
        h1 { font-size: 18px; text-align: center; margin: 16px 0; }
        h2 { font-size: 14px; margin: 18px 0 6px; }
        table { width: 100%; border-collapse: collapse; font-size: 12px; }
        .facts th { text-align: left; width: 22%; padding: 3px 6px; background: #f5f5f5; }
        .facts td { padding: 3px 6px; }
        .grid th, .grid td { border: 1px solid #bfbfbf; padding: 4px 6px; }
        .grid th { background: #f5f5f5; text-align: left; }
        .num { text-align: right; }
        .totals { margin-top: 12px; width: 50%; margin-left: 50%; }
        .totals th { text-align: left; padding: 3px 6px; }
        .totals td { text-align: right; padding: 3px 6px; }
        .signatures { display: flex; justify-content: space-between; margin-top: 56px; font-size: 12px; }
        .signatures div { width: 30%; text-align: center; border-top: 1px solid #595959; padding-top: 6px; }
        """;

    private const string RecordBody = """
        {% render 'letterhead' %}
        <h1>{{ texts.title }}</h1>
        <table class="facts">
          <tr><th>{{ texts.employee }}</th><td>{{ employee.name }}</td><th>{{ texts.registry_number }}</th><td>{{ employee.registry_number }}</td></tr>
          <tr><th>{{ texts.hire_date }}</th><td>{{ employee.hire_date }}</td><th>{{ texts.seniority_date }}</th><td>{{ employee.seniority_date }}</td></tr>
          <tr><th>{{ texts.job }}</th><td>{{ employee.job }}</td><th>{{ texts.seniority }}</th><td>{{ employee.seniority_years }} {{ texts.years }}</td></tr>
          <tr><th>{{ texts.unit }}</th><td colspan="3">{{ employee.unit }}</td></tr>
        </table>
        <h2>{{ texts.entitlements }}</h2>
        <table class="grid">
          <thead><tr><th>{{ texts.date }}</th><th>{{ texts.kind }}</th><th>{{ texts.note }}</th><th class="num">{{ texts.days }}</th></tr></thead>
          <tbody>
          {% for item in entitlements %}<tr><td>{{ item.date }}</td><td>{{ item.kind }}</td><td>{{ item.note }}</td><td class="num">{{ item.days }}</td></tr>
          {% else %}<tr><td colspan="4">{{ texts.none }}</td></tr>{% endfor %}
          </tbody>
        </table>
        <h2>{{ texts.leaves }}</h2>
        <table class="grid">
          <thead><tr><th>{{ texts.number }}</th><th>{{ texts.start }}</th><th>{{ texts.end }}</th><th class="num">{{ texts.travel_days }}</th><th class="num">{{ texts.days }}</th></tr></thead>
          <tbody>
          {% for item in leaves %}<tr><td>{{ item.number }}</td><td>{{ item.start }}</td><td>{{ item.end }}</td><td class="num">{{ item.travel_days }}</td><td class="num">{{ item.days }}</td></tr>
          {% else %}<tr><td colspan="5">{{ texts.none }}</td></tr>{% endfor %}
          </tbody>
        </table>
        <table class="totals">
          <tr><th>{{ texts.earned }}</th><td>{{ totals.earned }}</td></tr>
          <tr><th>{{ texts.used }}</th><td>{{ totals.used }}</td></tr>
          <tr><th>{{ texts.balance }} ({{ totals.as_of }})</th><td><strong>{{ totals.balance }}</strong></td></tr>
        </table>
        <div class="signatures">
          <div>{{ texts.employee_signature }}<br>{{ employee.name }}</div>
          <div>{{ texts.employer_signature }}</div>
        </div>
        """;

    private const string FormBody = """
        {% render 'letterhead' %}
        <h1>{{ texts.title }}</h1>
        <table class="facts">
          <tr><th>{{ texts.number }}</th><td>{{ request.number }}</td><th>{{ texts.status }}</th><td>{{ request.status }}</td></tr>
          <tr><th>{{ texts.employee }}</th><td>{{ employee.name }}</td><th>{{ texts.registry_number }}</th><td>{{ employee.registry_number }}</td></tr>
          <tr><th>{{ texts.job }}</th><td>{{ employee.job }}</td><th>{{ texts.unit }}</th><td>{{ employee.unit }}</td></tr>
        </table>
        <h2>{{ texts.leave }}</h2>
        <table class="grid">
          <tr><th>{{ texts.type }}</th><td>{{ request.type }}</td></tr>
          <tr><th>{{ texts.period }}</th><td>{{ request.start }} – {{ request.end }}</td></tr>
          <tr><th>{{ texts.length }}</th><td>{{ request.length }}</td></tr>
          {% if request.travel_days != "0" %}<tr><th>{{ texts.travel_days }}</th><td>{{ request.travel_days }}</td></tr>{% endif %}
          <tr><th>{{ texts.address }}</th><td>{{ request.address }}</td></tr>
          <tr><th>{{ texts.phone }}</th><td>{{ request.phone }}</td></tr>
          <tr><th>{{ texts.substitute }}</th><td>{{ request.substitute }}</td></tr>
          <tr><th>{{ texts.note }}</th><td>{{ request.note }}</td></tr>
          <tr><th>{{ texts.submitted_at }}</th><td>{{ request.submitted_at }}</td></tr>
        </table>
        <div class="signatures">
          <div>{{ texts.requester_signature }}<br>{{ employee.name }}</div>
          <div>{{ texts.manager_signature }}</div>
          <div>{{ texts.hr_signature }}</div>
        </div>
        """;

    public IEnumerable<DocumentTypeDefinition> GetDocumentTypes() =>
    [
        new(
            LeaveRecord,
            "hr.documents.leave_record.name",
            RecordBody,
            Css,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["employee"] = SampleEmployee(),
                ["entitlements"] = new List<object?>
                {
                    Row(("date", "16.03.2025"), ("kind", "Hak ediş"), ("note", "2025-03-16"), ("days", "14")),
                    Row(("date", "16.03.2026"), ("kind", "Hak ediş"), ("note", "2026-03-16"), ("days", "20")),
                },
                ["leaves"] = new List<object?>
                {
                    Row(("number", "IZN-2026-000012"), ("start", "04.05.2026"), ("end", "08.05.2026"), ("travel_days", "0"), ("days", "5")),
                },
                ["totals"] = Row(("earned", "34"), ("used", "5"), ("balance", "29"), ("as_of", "24.10.2026")),
            },
            [
                new("employee.name", "hr.documents.fields.employee_name"),
                new("employee.registry_number", "hr.documents.fields.registry_number"),
                new("employee.hire_date", "hr.documents.fields.hire_date"),
                new("employee.seniority_date", "hr.documents.fields.seniority_date"),
                new("employee.seniority_years", "hr.documents.fields.seniority_years"),
                new("employee.job", "hr.documents.fields.job"),
                new("employee.unit", "hr.documents.fields.unit"),
                new("entitlements", "hr.documents.leave_record.fields.entitlements"),
                new("leaves", "hr.documents.leave_record.fields.leaves"),
                new("totals", "hr.documents.leave_record.fields.totals"),
            ]),
        new(
            LeaveForm,
            "hr.documents.leave_form.name",
            FormBody,
            Css,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["employee"] = SampleEmployee(),
                ["request"] = Row(
                    ("number", "IZN-2026-000012"),
                    ("status", "Onaylandı"),
                    ("type", "Yıllık ücretli izin"),
                    ("start", "04.05.2026"),
                    ("end", "08.05.2026"),
                    ("length", "5 gün"),
                    ("travel_days", "0"),
                    ("address", "Çiçek Sok. No:3 Çeşme / İzmir"),
                    ("phone", "0532 000 00 00"),
                    ("substitute", "Mehmet Kaya"),
                    ("note", "Aile ziyareti"),
                    ("submitted_at", "20.04.2026")),
            },
            [
                new("employee.name", "hr.documents.fields.employee_name"),
                new("employee.registry_number", "hr.documents.fields.registry_number"),
                new("employee.job", "hr.documents.fields.job"),
                new("employee.unit", "hr.documents.fields.unit"),
                new("request", "hr.documents.leave_form.fields.request"),
            ]),
    ];

    private static Dictionary<string, object?> SampleEmployee() =>
        Row(
            ("name", "Ayşe Yılmaz"),
            ("registry_number", "000042"),
            ("hire_date", "16.03.2021"),
            ("seniority_date", "16.03.2021"),
            ("seniority_years", "5"),
            ("job", "Satış Uzmanı"),
            ("unit", "Satış Direktörlüğü"));

    internal static Dictionary<string, object?> Row(params (string Key, object? Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
}

/// <summary>Builds the models of the leave documents and prints them (D36, D46).</summary>
public sealed class LeaveDocumentPrinter(
    IHrDbContext db,
    IDocumentGenerator generator,
    IOrganizationLookup organization,
    ICurrentUser currentUser,
    ITextLocalizer localizer,
    TimeProvider timeProvider)
{
    private string Culture => string.IsNullOrWhiteSpace(currentUser.Culture) ? "tr-TR" : currentUser.Culture;

    public async Task<Result<HrFileDownload>> RecordAsync(Employee employee, Employment employment, CancellationToken cancellationToken)
    {
        var culture = CultureInfo.GetCultureInfo(Culture);
        var today = await LeaveTypeRules.TodayAsync(organization, timeProvider, employment.CompanyId, cancellationToken);
        var annual = await LeaveBalanceTypes.AnnualAsync(db, employment.CompanyId, cancellationToken);
        var account = annual is null
            ? null
            : await db.LeaveAccounts.AsNoTracking().Include(a => a.Entries)
                .SingleOrDefaultAsync(a => a.EmploymentId == employment.Id && a.LeaveTypeCode == annual.Code, cancellationToken);
        var leaves = annual is null
            ? []
            : await db.LeaveRequests.AsNoTracking()
                .Where(r => r.EmploymentId == employment.Id && r.DeductsFrom == annual.Code && LeaveReportRules.Approved.Contains(r.Status))
                .OrderBy(r => r.StartDate)
                .ToListAsync(cancellationToken);
        var earned = (account?.Entries ?? [])
            .Where(e => e.Quantity > 0 && e.Source is not (LeaveLedgerSource.Request or LeaveLedgerSource.RequestCancel or LeaveLedgerSource.Termination))
            .OrderBy(e => e.Date)
            .ToList();
        var years = CompletedYears(employment.SeniorityDate, today);
        var model = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["employee"] = await EmployeeAsync(employee, employment, today, culture, cancellationToken, years),
            ["entitlements"] = earned
                .Select(e => (object?)HrDocumentTypes.Row(
                    ("date", Date(e.Date, culture)),
                    ("kind", localizer.Get($"web.hr.leave_balances.sources.{e.Source}", culture: Culture)),
                    ("note", e.Note ?? e.PeriodKey ?? string.Empty),
                    ("days", Number(e.Quantity, culture))))
                .ToList(),
            ["leaves"] = leaves
                .Select(r => (object?)HrDocumentTypes.Row(
                    ("number", r.Number),
                    ("start", Date(r.StartDate, culture)),
                    ("end", Date(r.EndDate, culture)),
                    ("travel_days", r.TravelDays.ToString(culture)),
                    ("days", Number(r.Days, culture))))
                .ToList(),
            ["totals"] = HrDocumentTypes.Row(
                ("earned", Number(account?.Earned ?? 0, culture)),
                ("used", Number(account?.Used ?? 0, culture)),
                ("balance", Number(account?.Balance ?? 0, culture)),
                ("as_of", Date(today, culture))),
        };
        return await PrintAsync(
            HrDocumentTypes.LeaveRecord,
            employment.CompanyId,
            model,
            localizer.Get("hr.documents.leave_record.file_name", new Dictionary<string, object?> { ["registry"] = employment.RegistryNumber }, Culture),
            cancellationToken);
    }

    public async Task<Result<HrFileDownload>> FormAsync(LeaveRequest request, CancellationToken cancellationToken)
    {
        var culture = CultureInfo.GetCultureInfo(Culture);
        if (await LeaveRequestRules.EmploymentAsync(db, request.EmploymentId.Value, cancellationToken) is not { } person)
        {
            return CommonErrors.NotFound;
        }

        var today = await LeaveTypeRules.TodayAsync(organization, timeProvider, request.CompanyId, cancellationToken);
        var type = await db.LeaveTypes.AsNoTracking().Where(t => t.Id == request.LeaveTypeId).Select(t => t.Name).SingleOrDefaultAsync(cancellationToken);
        var substitute = request.SubstituteEmployeeId is { } substituteId
            ? await db.Employees.AsNoTracking().Where(e => e.Id == substituteId).Select(e => e.FirstName + " " + e.LastName).SingleOrDefaultAsync(cancellationToken)
            : null;
        var model = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["employee"] = await EmployeeAsync(person.Employee, person.Employment, today, culture, cancellationToken),
            ["request"] = HrDocumentTypes.Row(
                ("number", request.Number),
                ("status", localizer.Get($"hr.leave_requests.status.{request.Status}", culture: Culture)),
                ("type", type?.Get(Culture, "tr-TR") ?? request.LeaveTypeCode),
                ("start", Date(request.StartDate, culture)),
                ("end", Date(request.EndDate, culture)),
                ("length", localizer.Get(
                    request.Basis == DurationBasis.Hours ? "hr.leave_requests.length_hours" : "hr.leave_requests.length_days",
                    new Dictionary<string, object?> { ["quantity"] = Number(request.Quantity, culture) },
                    Culture)),
                ("travel_days", request.TravelDays.ToString(culture)),
                ("address", request.Address ?? string.Empty),
                ("phone", request.Phone ?? string.Empty),
                ("substitute", substitute ?? string.Empty),
                ("note", request.Note ?? string.Empty),
                ("submitted_at", request.SubmittedAt is { } at ? Date(DateOnly.FromDateTime(at.UtcDateTime), culture) : string.Empty)),
        };
        return await PrintAsync(
            HrDocumentTypes.LeaveForm,
            request.CompanyId,
            model,
            localizer.Get("hr.documents.leave_form.file_name", new Dictionary<string, object?> { ["number"] = request.Number }, Culture),
            cancellationToken);
    }

    private async Task<Dictionary<string, object?>> EmployeeAsync(
        Employee employee,
        Employment employment,
        DateOnly today,
        CultureInfo culture,
        CancellationToken cancellationToken,
        int? years = null)
    {
        var assignment = employment.AssignmentOn(today) ?? employment.LatestAssignment;
        var names = await EmployeeNames.LoadAsync(
            db,
            organization,
            assignment is null ? [] : [assignment.PositionId],
            assignment is null ? [] : [assignment.JobId],
            [],
            [],
            cancellationToken);
        return HrDocumentTypes.Row(
            ("name", employee.FullName),
            ("registry_number", employment.RegistryNumber),
            ("hire_date", Date(employment.HireDate, culture)),
            ("seniority_date", Date(employment.SeniorityDate, culture)),
            ("seniority_years", (years ?? CompletedYears(employment.SeniorityDate, today)).ToString(culture)),
            ("job", names.JobName(assignment?.JobId) ?? string.Empty),
            ("unit", names.UnitName(names.UnitOf(assignment?.PositionId)) ?? string.Empty));
    }

    private async Task<Result<HrFileDownload>> PrintAsync(
        string documentType,
        Guid companyId,
        Dictionary<string, object?> model,
        string fileName,
        CancellationToken cancellationToken)
    {
        var printed = await generator.GetPdfAsync(new DocumentRequest(documentType, companyId, Culture, model), cancellationToken);
        if (printed.IsFailure)
        {
            return printed.Error;
        }

        var content = printed.Value.Content;
        return new HrFileDownload(fileName, "application/pdf", content.Length, new MemoryStream(content));
    }

    private static int CompletedYears(DateOnly from, DateOnly today)
    {
        var years = today.Year - from.Year;
        return from.AddYears(years) > today ? years - 1 : years;
    }

    private static string Date(DateOnly date, CultureInfo culture) => date.ToString("d", culture);

    private static string Number(decimal value, CultureInfo culture) => value.ToString("0.##", culture);
}

internal sealed class GetLeaveRecordDocumentQueryHandler(IHrDbContext db, IAccessScopeProvider scopes, LeaveDocumentPrinter printer)
    : IQueryHandler<GetLeaveRecordDocumentQuery, HrFileDownload>
{
    public async Task<Result<HrFileDownload>> Handle(GetLeaveRecordDocumentQuery request, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ForCompaniesAsync(scopes, request.Permission, cancellationToken);
        var person = await LeaveRequestRules.EmploymentAsync(db, request.EmploymentId, cancellationToken);
        if (person is not { } found || found.Employee.Id != EmployeeId.From(request.EmployeeId) || !scope.CoversCompany(found.Employment.CompanyId))
        {
            return CommonErrors.NotFound;
        }

        return await printer.RecordAsync(found.Employee, found.Employment, cancellationToken);
    }
}

internal sealed class GetLeaveRequestFormQueryHandler(LeaveRequestAccess access, LeaveDocumentPrinter printer) : IQueryHandler<GetLeaveRequestFormQuery, HrFileDownload>
{
    public async Task<Result<HrFileDownload>> Handle(GetLeaveRequestFormQuery request, CancellationToken cancellationToken) =>
        await access.FindAsync(request.Permission, request.Id, tracked: false, cancellationToken) is { } leave
            ? await printer.FormAsync(leave, cancellationToken)
            : CommonErrors.NotFound;
}

internal sealed class GetMyLeaveRequestFormQueryHandler(IHrDbContext db, ICurrentUser currentUser, LeaveDocumentPrinter printer)
    : IQueryHandler<GetMyLeaveRequestFormQuery, HrFileDownload>
{
    public async Task<Result<HrFileDownload>> Handle(GetMyLeaveRequestFormQuery request, CancellationToken cancellationToken) =>
        await MyLeave.Mine(db, currentUser).AsNoTracking().SingleOrDefaultAsync(r => r.Id == LeaveRequestId.From(request.Id), cancellationToken) is { } leave
            ? await printer.FormAsync(leave, cancellationToken)
            : CommonErrors.NotFound;
}
