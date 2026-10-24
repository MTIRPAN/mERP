using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Exports;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence;
using Merp.Modules.HR.Application.Employees;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Leave;

/// <summary>
/// Balances as of a day (PLAN L6, D46): earned, used and left of each balance of the employments in scope, from the
/// movements up to that day. Balances closed before the day are left out.
/// </summary>
public sealed record GetLeaveBalanceReportQuery(
    PageRequest Paging,
    DateOnly AsOf,
    Guid? CompanyId = null,
    string? LeaveTypeCode = null,
    string? Search = null) : IQuery<PagedResult<LeaveBalanceReportRow>>, IRequirePermission
{
    public const string DefaultSort = "employeeName";

    internal static readonly SortMap<BalanceReportRow> Sorts = SortMap<BalanceReportRow>.By(r => r.AccountId)
        .Add("employeeName", r => r.EmployeeName)
        .Add("registryNumber", r => r.RegistryNumber)
        .Add("earned", r => r.Earned)
        .Add("used", r => r.Used)
        .Add("balance", r => r.Balance);

    public string Permission => HrPermissions.Leave.Reports.Read;
}

public sealed record LeaveBalanceReportRow(
    Guid EmployeeId,
    Guid EmploymentId,
    string EmployeeName,
    string RegistryNumber,
    Guid CompanyId,
    string? CompanyName,
    string LeaveTypeCode,
    string LeaveTypeName,
    decimal Earned,
    decimal Used,
    decimal Balance);

/// <summary>
/// Leave taken in a period, by person and type (PLAN L6, D46): approved leaves that start in the period, their count
/// and days. Search matches the person's name.
/// </summary>
public sealed record GetLeaveUsageReportQuery(
    PageRequest Paging,
    DateOnly From,
    DateOnly To,
    Guid? CompanyId = null,
    string? LeaveTypeCode = null,
    string? Search = null) : IQuery<PagedResult<LeaveUsageReportRow>>, IRequirePermission
{
    public const string DefaultSort = "-days";

    internal static readonly SortMap<UsageReportRow> Sorts = SortMap<UsageReportRow>.By(r => r.EmploymentId)
        .Add("days", r => r.Days)
        .Add("requests", r => r.Requests)
        .Add("leaveTypeCode", r => r.LeaveTypeCode);

    public string Permission => HrPermissions.Leave.Reports.Read;
}

public sealed record LeaveUsageReportRow(
    Guid EmployeeId,
    Guid EmploymentId,
    string EmployeeName,
    string RegistryNumber,
    Guid CompanyId,
    string? CompanyName,
    string LeaveTypeCode,
    string LeaveTypeName,
    int Requests,
    decimal Days);

/// <summary>
/// Leaves that start in the next <see cref="Days"/> days from <see cref="From"/> (default today), approved, and with
/// <see cref="IncludePending"/> those waiting for approval too (PLAN L6, D46).
/// </summary>
public sealed record GetUpcomingLeavesReportQuery(
    PageRequest Paging,
    DateOnly? From = null,
    int Days = 30,
    Guid? CompanyId = null,
    string? LeaveTypeCode = null,
    bool IncludePending = false) : IQuery<PagedResult<LeaveRequestListItem>>, IRequirePermission
{
    public const string DefaultSort = "startDate";
    public const int MaxDays = 366;

    internal static readonly SortMap<LeaveRequest> Sorts = SortMap<LeaveRequest>.By(r => r.Id)
        .Add("startDate", r => r.StartDate)
        .Add("number", r => r.Number);

    public string Permission => HrPermissions.Leave.Reports.Read;
}

/// <summary>
/// People who have not taken leave from a balance for <see cref="Months"/> months (PLAN L6, D46): open employments
/// older than that whose last such leave ended before it, or who never took one, with their balance now.
/// </summary>
public sealed record GetLongTimeNoLeaveReportQuery(
    PageRequest Paging,
    int Months = 12,
    Guid? CompanyId = null,
    string? Search = null) : IQuery<PagedResult<LongTimeNoLeaveRow>>, IRequirePermission
{
    public const string DefaultSort = "lastLeaveEnd";

    internal static readonly SortMap<NoLeaveRow> Sorts = SortMap<NoLeaveRow>.By(r => r.EmploymentId)
        .Add("lastLeaveEnd", r => r.LastLeaveEnd)
        .Add("employeeName", r => r.FirstName)
        .Add("hireDate", r => r.HireDate)
        .Add("balance", r => r.Balance);

    public string Permission => HrPermissions.Leave.Reports.Read;
}

/// <remarks>
/// LastLeaveEnd is the last day of the last leave taken from a balance and DaysSince the days since then, both null
/// when none was taken; Balance is what the open balances hold now.
/// </remarks>
public sealed record LongTimeNoLeaveRow(
    Guid EmployeeId,
    Guid EmploymentId,
    string EmployeeName,
    string RegistryNumber,
    Guid CompanyId,
    string? CompanyName,
    string? OrgUnitName,
    DateOnly HireDate,
    DateOnly? LastLeaveEnd,
    int? DaysSince,
    decimal Balance);

/// <summary>The columns of the leave reports' Excel files; keys are those of the report columns on the screen.</summary>
public static class LeaveReportExports
{
    private const string Prefix = "web.hr.leave_reports.columns.";

    public static readonly IReadOnlyList<ExportColumn<LeaveBalanceReportRow>> Balances =
    [
        ExportColumn<LeaveBalanceReportRow>.Of("registryNumber", Prefix + "registry_number", r => r.RegistryNumber),
        ExportColumn<LeaveBalanceReportRow>.Of("employeeName", Prefix + "employee", r => r.EmployeeName),
        ExportColumn<LeaveBalanceReportRow>.Of("companyName", Prefix + "company", r => r.CompanyName),
        ExportColumn<LeaveBalanceReportRow>.Of("leaveTypeName", Prefix + "leave_type", r => r.LeaveTypeName),
        ExportColumn<LeaveBalanceReportRow>.Of("earned", Prefix + "earned", r => r.Earned),
        ExportColumn<LeaveBalanceReportRow>.Of("used", Prefix + "used", r => r.Used),
        ExportColumn<LeaveBalanceReportRow>.Of("balance", Prefix + "balance", r => r.Balance),
    ];

    public static readonly IReadOnlyList<ExportColumn<LeaveUsageReportRow>> Usage =
    [
        ExportColumn<LeaveUsageReportRow>.Of("registryNumber", Prefix + "registry_number", r => r.RegistryNumber),
        ExportColumn<LeaveUsageReportRow>.Of("employeeName", Prefix + "employee", r => r.EmployeeName),
        ExportColumn<LeaveUsageReportRow>.Of("companyName", Prefix + "company", r => r.CompanyName),
        ExportColumn<LeaveUsageReportRow>.Of("leaveTypeName", Prefix + "leave_type", r => r.LeaveTypeName),
        ExportColumn<LeaveUsageReportRow>.Of("requests", Prefix + "requests", r => r.Requests),
        ExportColumn<LeaveUsageReportRow>.Of("days", Prefix + "days", r => r.Days),
    ];

    public static readonly IReadOnlyList<ExportColumn<LeaveRequestListItem>> Upcoming =
    [
        ExportColumn<LeaveRequestListItem>.Of("number", Prefix + "number", r => r.Number),
        ExportColumn<LeaveRequestListItem>.Of("employeeName", Prefix + "employee", r => r.EmployeeName),
        ExportColumn<LeaveRequestListItem>.Of("companyName", Prefix + "company", r => r.CompanyName),
        ExportColumn<LeaveRequestListItem>.Of("leaveTypeName", Prefix + "leave_type", r => r.LeaveTypeName),
        ExportColumn<LeaveRequestListItem>.Of("startDate", Prefix + "start_date", r => r.StartDate),
        ExportColumn<LeaveRequestListItem>.Of("endDate", Prefix + "end_date", r => r.EndDate),
        ExportColumn<LeaveRequestListItem>.Of("days", Prefix + "days", r => r.Days),
        ExportColumn<LeaveRequestListItem>.Of("status", Prefix + "status", r => r.Status, "web.hr.leave_requests.status."),
    ];

    public static readonly IReadOnlyList<ExportColumn<LongTimeNoLeaveRow>> LongTimeNoLeave =
    [
        ExportColumn<LongTimeNoLeaveRow>.Of("registryNumber", Prefix + "registry_number", r => r.RegistryNumber),
        ExportColumn<LongTimeNoLeaveRow>.Of("employeeName", Prefix + "employee", r => r.EmployeeName),
        ExportColumn<LongTimeNoLeaveRow>.Of("companyName", Prefix + "company", r => r.CompanyName),
        ExportColumn<LongTimeNoLeaveRow>.Of("orgUnitName", Prefix + "org_unit", r => r.OrgUnitName),
        ExportColumn<LongTimeNoLeaveRow>.Of("hireDate", Prefix + "hire_date", r => r.HireDate),
        ExportColumn<LongTimeNoLeaveRow>.Of("lastLeaveEnd", Prefix + "last_leave_end", r => r.LastLeaveEnd),
        ExportColumn<LongTimeNoLeaveRow>.Of("daysSince", Prefix + "days_since", r => r.DaysSince),
        ExportColumn<LongTimeNoLeaveRow>.Of("balance", Prefix + "balance", r => r.Balance),
    ];
}

/// <summary>A balance with its sums up to the report day, as the database computes it.</summary>
internal sealed class BalanceReportRow
{
    public LeaveAccountId AccountId { get; init; }

    public EmployeeId EmployeeId { get; init; }

    public EmploymentId EmploymentId { get; init; }

    public Guid CompanyId { get; init; }

    public string LeaveTypeCode { get; init; } = null!;

    public string EmployeeName { get; init; } = null!;

    public string RegistryNumber { get; init; } = null!;

    public decimal Earned { get; init; }

    public decimal Used { get; init; }

    public decimal Balance { get; init; }
}

/// <summary>The approved leaves of a person and a type in a period.</summary>
internal sealed class UsageReportRow
{
    public EmployeeId EmployeeId { get; init; }

    public EmploymentId EmploymentId { get; init; }

    public Guid CompanyId { get; init; }

    public string LeaveTypeCode { get; init; } = null!;

    public int Requests { get; init; }

    public decimal Days { get; init; }
}

/// <summary>An open employment with its last leave from a balance and its balance now.</summary>
internal sealed class NoLeaveRow
{
    public EmployeeId EmployeeId { get; init; }

    public EmploymentId EmploymentId { get; init; }

    public string FirstName { get; init; } = null!;

    public string LastName { get; init; } = null!;

    public string RegistryNumber { get; init; } = null!;

    public Guid CompanyId { get; init; }

    public Domain.Organization.PositionId? PositionId { get; init; }

    public DateOnly HireDate { get; init; }

    public DateOnly? LastLeaveEnd { get; init; }

    public decimal Balance { get; init; }
}

internal static class LeaveReportRules
{
    public static readonly LeaveRequestStatus[] Approved = [LeaveRequestStatus.Approved, LeaveRequestStatus.CancellationPending];

    public static async Task<Dictionary<string, string>> TypeNamesAsync(IHrDbContext db, ICurrentUser currentUser, IEnumerable<string> codes, CancellationToken cancellationToken)
    {
        var list = codes.Distinct().ToList();
        return (await db.LeaveTypes.AsNoTracking().Where(t => list.Contains(t.Code)).ToListAsync(cancellationToken))
            .ToDictionary(t => t.Code, t => t.Name.Get(currentUser.Culture, "tr-TR"), StringComparer.Ordinal);
    }

    public static async Task<Dictionary<Guid, string>> CompanyNamesAsync(IOrganizationLookup organization, IEnumerable<Guid> ids, CancellationToken cancellationToken) =>
        (await organization.GetCompaniesAsync([.. ids.Distinct()], cancellationToken)).ToDictionary(c => c.Id, c => c.ShortName);

    /// <summary>Names and registry numbers of the employments on a page.</summary>
    public static async Task<Dictionary<EmploymentId, (string Name, string Registry)>> PeopleAsync(IHrDbContext db, IEnumerable<EmploymentId> ids, CancellationToken cancellationToken)
    {
        var list = ids.Distinct().ToList();
        return (await db.Employees.AsNoTracking()
                .SelectMany(e => e.Employments, (e, m) => new { m.Id, e.FirstName, e.LastName, m.RegistryNumber })
                .Where(x => list.Contains(x.Id))
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.Id, x => ($"{x.FirstName} {x.LastName}", x.RegistryNumber));
    }

    public static string? Code(string? code) => string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
}

internal sealed class GetLeaveBalanceReportQueryValidator : AbstractValidator<GetLeaveBalanceReportQuery>
{
    public GetLeaveBalanceReportQueryValidator()
    {
        RuleFor(q => q.AsOf).NotEmpty();
        RuleFor(q => q.Paging.Sort).Must(GetLeaveBalanceReportQuery.Sorts.IsValid).WithErrorCode(CommonErrors.InvalidSort.Code).OverridePropertyName("sort");
    }
}

internal sealed class GetLeaveUsageReportQueryValidator : AbstractValidator<GetLeaveUsageReportQuery>
{
    public GetLeaveUsageReportQueryValidator()
    {
        RuleFor(q => q.From).NotEmpty();
        RuleFor(q => q.To).GreaterThanOrEqualTo(q => q.From).WithErrorCode(CommonErrors.InvalidDateRange.Code);
        RuleFor(q => q.Paging.Sort).Must(GetLeaveUsageReportQuery.Sorts.IsValid).WithErrorCode(CommonErrors.InvalidSort.Code).OverridePropertyName("sort");
    }
}

internal sealed class GetUpcomingLeavesReportQueryValidator : AbstractValidator<GetUpcomingLeavesReportQuery>
{
    public GetUpcomingLeavesReportQueryValidator()
    {
        RuleFor(q => q.Days).InclusiveBetween(1, GetUpcomingLeavesReportQuery.MaxDays);
        RuleFor(q => q.Paging.Sort).Must(GetUpcomingLeavesReportQuery.Sorts.IsValid).WithErrorCode(CommonErrors.InvalidSort.Code).OverridePropertyName("sort");
    }
}

internal sealed class GetLongTimeNoLeaveReportQueryValidator : AbstractValidator<GetLongTimeNoLeaveReportQuery>
{
    public GetLongTimeNoLeaveReportQueryValidator()
    {
        RuleFor(q => q.Months).InclusiveBetween(1, 120);
        RuleFor(q => q.Paging.Sort).Must(GetLongTimeNoLeaveReportQuery.Sorts.IsValid).WithErrorCode(CommonErrors.InvalidSort.Code).OverridePropertyName("sort");
    }
}

internal sealed class GetLeaveBalanceReportQueryHandler(IHrDbContext db, LeaveRequestAccess access, IOrganizationLookup organization, ICurrentUser currentUser)
    : IQueryHandler<GetLeaveBalanceReportQuery, PagedResult<LeaveBalanceReportRow>>
{
    public async Task<Result<PagedResult<LeaveBalanceReportRow>>> Handle(GetLeaveBalanceReportQuery request, CancellationToken cancellationToken)
    {
        var visible = await access.EmploymentsAsync(request.Permission, cancellationToken);
        var asOf = request.AsOf;
        var accounts = db.LeaveAccounts.AsNoTracking().Where(a => visible.Contains(a.EmploymentId) && (a.ClosedOn == null || a.ClosedOn >= asOf));
        if (request.CompanyId is { } companyId)
        {
            accounts = accounts.Where(a => a.CompanyId == companyId);
        }

        if (LeaveReportRules.Code(request.LeaveTypeCode) is { } code)
        {
            accounts = accounts.Where(a => a.LeaveTypeCode == code);
        }

        var employees = db.Employees.AsNoTracking();
        var rows = accounts.Select(a => new BalanceReportRow
        {
            AccountId = a.Id,
            EmployeeId = a.EmployeeId,
            EmploymentId = a.EmploymentId,
            CompanyId = a.CompanyId,
            LeaveTypeCode = a.LeaveTypeCode,
            EmployeeName = employees.Where(e => e.Id == a.EmployeeId).Select(e => e.FirstName + " " + e.LastName).First(),
            RegistryNumber = employees.SelectMany(e => e.Employments).Where(m => m.Id == a.EmploymentId).Select(m => m.RegistryNumber).First(),
            Balance = a.Entries.Where(e => e.Date <= asOf).Sum(e => e.Quantity),
            Earned = a.Entries
                .Where(e => e.Date <= asOf && e.Quantity > 0
                    && e.Source != LeaveLedgerSource.Request && e.Source != LeaveLedgerSource.RequestCancel && e.Source != LeaveLedgerSource.Termination)
                .Sum(e => e.Quantity),
            Used = -a.Entries.Where(e => e.Date <= asOf && (e.Source == LeaveLedgerSource.Request || e.Source == LeaveLedgerSource.RequestCancel)).Sum(e => e.Quantity),
        });
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = TextSearch.ContainsPattern(request.Search);
            rows = rows.Where(r => TextSearch.Matches(r.EmployeeName, pattern) || TextSearch.Matches(r.RegistryNumber, pattern));
        }

        var page = await GetLeaveBalanceReportQuery.Sorts.Apply(rows, request.Paging.Sort, GetLeaveBalanceReportQuery.DefaultSort)
            .ToPagedResultAsync(request.Paging, cancellationToken);
        var types = await LeaveReportRules.TypeNamesAsync(db, currentUser, page.Items.Select(r => r.LeaveTypeCode), cancellationToken);
        var companies = await LeaveReportRules.CompanyNamesAsync(organization, page.Items.Select(r => r.CompanyId), cancellationToken);
        return new PagedResult<LeaveBalanceReportRow>(
            [.. page.Items.Select(r => new LeaveBalanceReportRow(
                r.EmployeeId.Value,
                r.EmploymentId.Value,
                r.EmployeeName,
                r.RegistryNumber,
                r.CompanyId,
                companies.GetValueOrDefault(r.CompanyId),
                r.LeaveTypeCode,
                types.GetValueOrDefault(r.LeaveTypeCode, r.LeaveTypeCode),
                r.Earned,
                r.Used,
                r.Balance))],
            page.Page,
            page.PageSize,
            page.TotalCount);
    }
}

internal sealed class GetLeaveUsageReportQueryHandler(IHrDbContext db, LeaveRequestAccess access, IOrganizationLookup organization, ICurrentUser currentUser)
    : IQueryHandler<GetLeaveUsageReportQuery, PagedResult<LeaveUsageReportRow>>
{
    public async Task<Result<PagedResult<LeaveUsageReportRow>>> Handle(GetLeaveUsageReportQuery request, CancellationToken cancellationToken)
    {
        var visible = await access.EmploymentsAsync(request.Permission, cancellationToken);
        var requests = db.LeaveRequests.AsNoTracking()
            .Where(r => visible.Contains(r.EmploymentId) && LeaveReportRules.Approved.Contains(r.Status) && r.StartDate >= request.From && r.StartDate <= request.To);
        if (request.CompanyId is { } companyId)
        {
            requests = requests.Where(r => r.CompanyId == companyId);
        }

        if (LeaveReportRules.Code(request.LeaveTypeCode) is { } code)
        {
            requests = requests.Where(r => r.LeaveTypeCode == code);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = TextSearch.ContainsPattern(request.Search);
            var people = db.Employees.Where(e => TextSearch.Matches(e.FirstName + " " + e.LastName, pattern)).Select(e => e.Id);
            requests = requests.Where(r => people.Contains(r.EmployeeId));
        }

        var rows = requests
            .GroupBy(r => new { r.EmployeeId, r.EmploymentId, r.CompanyId, r.LeaveTypeCode })
            .Select(g => new UsageReportRow
            {
                EmployeeId = g.Key.EmployeeId,
                EmploymentId = g.Key.EmploymentId,
                CompanyId = g.Key.CompanyId,
                LeaveTypeCode = g.Key.LeaveTypeCode,
                Requests = g.Count(),
                Days = g.Sum(r => r.Days),
            });
        var page = await GetLeaveUsageReportQuery.Sorts.Apply(rows, request.Paging.Sort, GetLeaveUsageReportQuery.DefaultSort)
            .ToPagedResultAsync(request.Paging, cancellationToken);
        var names = await LeaveReportRules.PeopleAsync(db, page.Items.Select(r => r.EmploymentId), cancellationToken);
        var types = await LeaveReportRules.TypeNamesAsync(db, currentUser, page.Items.Select(r => r.LeaveTypeCode), cancellationToken);
        var companies = await LeaveReportRules.CompanyNamesAsync(organization, page.Items.Select(r => r.CompanyId), cancellationToken);
        return new PagedResult<LeaveUsageReportRow>(
            [.. page.Items.Select(r => new LeaveUsageReportRow(
                r.EmployeeId.Value,
                r.EmploymentId.Value,
                names.GetValueOrDefault(r.EmploymentId).Name ?? string.Empty,
                names.GetValueOrDefault(r.EmploymentId).Registry ?? string.Empty,
                r.CompanyId,
                companies.GetValueOrDefault(r.CompanyId),
                r.LeaveTypeCode,
                types.GetValueOrDefault(r.LeaveTypeCode, r.LeaveTypeCode),
                r.Requests,
                r.Days))],
            page.Page,
            page.PageSize,
            page.TotalCount);
    }
}

internal sealed class GetUpcomingLeavesReportQueryHandler(
    IHrDbContext db,
    LeaveRequestAccess access,
    IOrganizationLookup organization,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IQueryHandler<GetUpcomingLeavesReportQuery, PagedResult<LeaveRequestListItem>>
{
    public async Task<Result<PagedResult<LeaveRequestListItem>>> Handle(GetUpcomingLeavesReportQuery request, CancellationToken cancellationToken)
    {
        var visible = await access.EmploymentsAsync(request.Permission, cancellationToken);
        var from = request.From ?? HrClock.Today(timeProvider, null);
        var until = from.AddDays(request.Days - 1);
        var statuses = request.IncludePending ? LeaveRequestStatuses.Blocking : LeaveReportRules.Approved;
        var requests = db.LeaveRequests.AsNoTracking()
            .Where(r => visible.Contains(r.EmploymentId) && statuses.Contains(r.Status) && r.StartDate >= from && r.StartDate <= until);
        if (request.CompanyId is { } companyId)
        {
            requests = requests.Where(r => r.CompanyId == companyId);
        }

        if (LeaveReportRules.Code(request.LeaveTypeCode) is { } code)
        {
            requests = requests.Where(r => r.LeaveTypeCode == code);
        }

        var page = await GetUpcomingLeavesReportQuery.Sorts.Apply(requests, request.Paging.Sort, GetUpcomingLeavesReportQuery.DefaultSort)
            .ToPagedResultAsync(request.Paging, cancellationToken);
        return new PagedResult<LeaveRequestListItem>(
            await LeaveRequestRules.ItemsAsync(db, organization, currentUser, page.Items, cancellationToken),
            page.Page,
            page.PageSize,
            page.TotalCount);
    }
}

internal sealed class GetLongTimeNoLeaveReportQueryHandler(
    IHrDbContext db,
    IAccessScopeProvider scopes,
    IPositionHolders holders,
    ICurrentUser currentUser,
    IOrganizationLookup organization,
    TimeProvider timeProvider) : IQueryHandler<GetLongTimeNoLeaveReportQuery, PagedResult<LongTimeNoLeaveRow>>
{
    public async Task<Result<PagedResult<LongTimeNoLeaveRow>>> Handle(GetLongTimeNoLeaveReportQuery request, CancellationToken cancellationToken)
    {
        var scope = await EmployeeScope.ResolveAsync(scopes, request.Permission, db, holders, currentUser, cancellationToken);
        var days = await CompanyDays.ResolveAsync(db, organization, timeProvider, cancellationToken);
        var today = HrClock.Today(timeProvider, null);
        var cutoff = today.AddMonths(-request.Months);
        var employments = scope.Filter(EmploymentRows.Query(db.Employees.AsNoTracking(), days), db)
            .Where(r => r.TerminationDate == null && r.HireDate <= cutoff);
        if (request.CompanyId is { } companyId)
        {
            employments = employments.Where(r => r.CompanyId == companyId);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = TextSearch.ContainsPattern(request.Search);
            employments = employments.Where(r => TextSearch.Matches(r.FirstName + " " + r.LastName, pattern) || TextSearch.Matches(r.RegistryNumber, pattern));
        }

        var approved = LeaveReportRules.Approved;
        var leaves = db.LeaveRequests.AsNoTracking();
        var accounts = db.LeaveAccounts.AsNoTracking();
        var rows = employments
            .Select(r => new NoLeaveRow
            {
                EmployeeId = r.EmployeeId,
                EmploymentId = r.EmploymentId,
                FirstName = r.FirstName,
                LastName = r.LastName,
                RegistryNumber = r.RegistryNumber,
                CompanyId = r.CompanyId,
                PositionId = r.PositionId,
                HireDate = r.HireDate,
                LastLeaveEnd = leaves
                    .Where(q => q.EmploymentId == r.EmploymentId && approved.Contains(q.Status) && q.DeductsFrom != null && q.StartDate <= today)
                    .Max(q => (DateOnly?)q.EndDate),
                Balance = accounts.Where(a => a.EmploymentId == r.EmploymentId && a.ClosedOn == null).SelectMany(a => a.Entries).Sum(e => (decimal?)e.Quantity) ?? 0,
            })
            .Where(r => r.LastLeaveEnd == null || r.LastLeaveEnd < cutoff);
        var page = await GetLongTimeNoLeaveReportQuery.Sorts.Apply(rows, request.Paging.Sort, GetLongTimeNoLeaveReportQuery.DefaultSort)
            .ToPagedResultAsync(request.Paging, cancellationToken);
        var names = await EmployeeNames.LoadAsync(
            db,
            organization,
            page.Items.Where(r => r.PositionId is not null).Select(r => r.PositionId!.Value),
            [],
            page.Items.Select(r => r.CompanyId),
            [],
            cancellationToken);
        return new PagedResult<LongTimeNoLeaveRow>(
            [.. page.Items.Select(r => new LongTimeNoLeaveRow(
                r.EmployeeId.Value,
                r.EmploymentId.Value,
                $"{r.FirstName} {r.LastName}",
                r.RegistryNumber,
                r.CompanyId,
                names.CompanyName(r.CompanyId),
                names.UnitName(names.UnitOf(r.PositionId)),
                r.HireDate,
                r.LastLeaveEnd,
                r.LastLeaveEnd is { } last ? today.DayNumber - last.DayNumber : null,
                r.Balance))],
            page.Page,
            page.PageSize,
            page.TotalCount);
    }
}
