using System.Globalization;
using System.Text.Json;
using FluentValidation;
using Merp.Application.Abstractions.Auditing;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence;
using Merp.Application.Persistence.Files;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Organization;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.Modules.Platform.Contracts.Users;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Employees;

/// <summary>
/// Employments in the caller's scope, one row per person and company, filtered and paged; the active company narrows
/// the list (D4). Without <see cref="Statuses"/> the list shows who works today (D38). The TCKN is always masked in
/// lists; it is shown on the card, where reading it is audited.
/// </summary>
/// <param name="Paging">Page and sort.</param>
/// <param name="CompanyId">Only employments of this company.</param>
/// <param name="BranchId">Only people whose assignment is in this branch.</param>
/// <param name="OrgUnitId">Only people whose position is in this unit.</param>
/// <param name="IncludeSubUnits">With <paramref name="OrgUnitId"/>: also the people of its sub units.</param>
/// <param name="PositionId">Only the holders of this position.</param>
/// <param name="JobId">Only people with this job.</param>
/// <param name="Statuses">Employment states on the company's day; Active when empty.</param>
/// <param name="Search">Part of the name or of the registry number, or a whole TCKN.</param>
/// <param name="CustomFields">JSON object of custom field codes and values the people must have.</param>
public sealed record GetEmployeesQuery(
    PageRequest Paging,
    Guid? CompanyId = null,
    Guid? BranchId = null,
    Guid? OrgUnitId = null,
    bool IncludeSubUnits = false,
    Guid? PositionId = null,
    Guid? JobId = null,
    IReadOnlyList<EmploymentStatus>? Statuses = null,
    string? Search = null,
    string? CustomFields = null) : IQuery<PagedResult<EmployeeListItem>>, IRequirePermission
{
    public const string DefaultSort = "firstName";

    internal static readonly SortMap<EmploymentRow> Sorts = SortMap<EmploymentRow>.By(r => r.EmploymentId)
        .Add("firstName", r => r.FirstName)
        .Add("lastName", r => r.LastName)
        .Add("registryNumber", r => r.RegistryNumber)
        .Add("hireDate", r => r.HireDate)
        .Add("terminationDate", r => r.TerminationDate)
        .Add("createdAt", r => r.CreatedAt);

    public string Permission => HrPermissions.Employees.Read;
}

/// <remarks>
/// Id is the person, EmploymentId the row. The position, job, unit and branch are those of the assignment on the
/// company's day (the first one before the hire date, the last one after leaving). PhotoFileId is downloaded from
/// <c>/api/hr/files/{id}</c>. CustomFields holds the custom field values by code, for optional list columns.
/// </remarks>
public sealed record EmployeeListItem(
    Guid Id,
    Guid EmploymentId,
    string FirstName,
    string LastName,
    string FullName,
    string Tckn,
    Guid CompanyId,
    string CompanyName,
    string RegistryNumber,
    DateOnly HireDate,
    DateOnly? TerminationDate,
    EmploymentStatus Status,
    bool IsPrimary,
    Guid? PositionId,
    string? PositionCode,
    Guid? JobId,
    string? JobName,
    Guid? OrgUnitId,
    string? OrgUnitName,
    Guid? BranchId,
    string? BranchName,
    Guid? PhotoFileId,
    IReadOnlyDictionary<string, JsonElement> CustomFields);

/// <summary>
/// The employee card (D38): the person, the employments the caller's scope reaches with their job history, and the
/// sensitive data. Without <c>hr.employees.sensitive.read</c> on the person the TCKN and the IBAN are masked and the
/// special category data is hidden; otherwise every value shown is recorded in the audit log (KVKK, D13).
/// </summary>
public sealed record GetEmployeeQuery(Guid Id) : IQuery<EmployeeDetails>, IRequirePermission
{
    public string Permission => HrPermissions.Employees.Read;
}

/// <remarks>
/// SensitiveVisible tells whether Tckn, Iban and Sensitive are the plain values. UserLinkState and User describe the
/// person's system user (D39); UserLinkError is the reason of a refused request in the caller's language. Employments
/// are newest first, their assignments too. Send Version back when updating the card (optimistic concurrency).
/// </remarks>
public sealed record EmployeeDetails(
    Guid Id,
    string Tckn,
    string FirstName,
    string LastName,
    string FullName,
    DateOnly? BirthDate,
    string? BirthPlace,
    Gender? Gender,
    string? Nationality,
    MaritalStatus? MaritalStatus,
    MilitaryStatus? MilitaryStatus,
    string? Iban,
    bool SensitiveVisible,
    Guid? UserId,
    UserLinkState UserLinkState,
    string? UserLinkError,
    EmployeeUserItem? User,
    Guid? PhotoFileId,
    IReadOnlyList<EmployeeEmail> Emails,
    IReadOnlyList<EmployeePhone> Phones,
    IReadOnlyList<EmployeeAddress> Addresses,
    IReadOnlyList<EmergencyContact> EmergencyContacts,
    IReadOnlyList<FamilyMember> FamilyMembers,
    IReadOnlyList<Education> Educations,
    IReadOnlyList<Certificate> Certificates,
    IReadOnlyList<LanguageSkill> Languages,
    IReadOnlyDictionary<string, JsonElement> CustomFields,
    EmployeeSensitiveItem Sensitive,
    IReadOnlyList<EmploymentItem> Employments,
    uint Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt);

/// <summary>The person's system user as Platform knows it; IsActive false means they cannot sign in.</summary>
public sealed record EmployeeUserItem(Guid Id, string UserName, string? Email, bool IsActive);

/// <summary>Special category data; Hidden and every value empty without the permission.</summary>
public sealed record EmployeeSensitiveItem(bool Hidden, int? DisabilityDegree, string? BloodType, string? HealthNotes, DateOnly? CriminalRecordDate);

/// <remarks>Status is on the company's day.</remarks>
public sealed record EmploymentItem(
    Guid Id,
    Guid CompanyId,
    string CompanyName,
    string RegistryNumber,
    DateOnly HireDate,
    DateOnly SeniorityDate,
    string? SeniorityReason,
    EmploymentType Type,
    DateOnly? ContractEndDate,
    decimal? WeeklyHours,
    DateOnly? ProbationEndDate,
    Guid? WorkScheduleId,
    Guid? LeavePolicyId,
    bool IsPrimary,
    EmploymentStatus Status,
    DateOnly? TerminationDate,
    string? SgkExitCode,
    string? SgkExitCodeName,
    string? TerminationReason,
    bool? Rehireable,
    string? TerminationNote,
    IReadOnlyList<JobAssignmentItem> Assignments);

/// <remarks>IsCurrent marks the assignment on the company's day.</remarks>
public sealed record JobAssignmentItem(
    Guid Id,
    Guid PositionId,
    string? PositionCode,
    Guid? OrgUnitId,
    string? OrgUnitName,
    Guid JobId,
    string? JobName,
    Guid? BranchId,
    string? BranchName,
    Guid? ManagerPositionId,
    string? ManagerPositionCode,
    string? Level,
    string? CostCenterCode,
    AssignmentReason Reason,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    bool IsCurrent);

internal sealed class GetEmployeesQueryValidator : AbstractValidator<GetEmployeesQuery>
{
    public GetEmployeesQueryValidator()
    {
        RuleFor(q => q.Paging.Sort).Must(GetEmployeesQuery.Sorts.IsValid).WithErrorCode(CommonErrors.InvalidSort.Code).OverridePropertyName("sort");
        RuleForEach(q => q.Statuses).IsInEnum().OverridePropertyName("status");
        RuleFor(q => q.CustomFields)
            .Must(json => CustomFieldFiltering.Parse(json, out var valid) is not null || valid)
            .WithErrorCode(CustomFieldFiltering.InvalidFilterCode);
    }
}

/// <summary>Names around employees, read once per page.</summary>
internal sealed class EmployeeNames
{
    private EmployeeNames()
    {
    }

    public IReadOnlyDictionary<PositionId, (string Code, OrgUnitId UnitId)> Positions { get; private init; } = new Dictionary<PositionId, (string, OrgUnitId)>();

    public IReadOnlyDictionary<OrgUnitId, string> Units { get; private init; } = new Dictionary<OrgUnitId, string>();

    public IReadOnlyDictionary<JobId, string> Jobs { get; private init; } = new Dictionary<JobId, string>();

    public IReadOnlyDictionary<Guid, CompanySummary> Companies { get; private init; } = new Dictionary<Guid, CompanySummary>();

    public IReadOnlyDictionary<Guid, BranchSummary> Branches { get; private init; } = new Dictionary<Guid, BranchSummary>();

    public static async Task<EmployeeNames> LoadAsync(
        IHrDbContext db,
        IOrganizationLookup organization,
        IEnumerable<PositionId> positionIds,
        IEnumerable<JobId> jobIds,
        IEnumerable<Guid> companyIds,
        IEnumerable<Guid> branchIds,
        CancellationToken cancellationToken)
    {
        var positionList = positionIds.Distinct().ToList();
        var jobList = jobIds.Distinct().ToList();
        var positions = await db.Positions.AsNoTracking()
            .Where(p => positionList.Contains(p.Id))
            .Select(p => new { p.Id, p.Code, p.OrgUnitId })
            .ToListAsync(cancellationToken);
        var unitIds = positions.Select(p => p.OrgUnitId).Distinct().ToList();
        var units = await db.OrgUnits.AsNoTracking()
            .Where(u => unitIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Name })
            .ToListAsync(cancellationToken);
        var jobs = await db.Jobs.AsNoTracking()
            .Where(j => jobList.Contains(j.Id))
            .Select(j => new { j.Id, j.Name })
            .ToListAsync(cancellationToken);
        var branchList = branchIds.Distinct().ToList();
        return new EmployeeNames
        {
            Positions = positions.ToDictionary(p => p.Id, p => (p.Code, p.OrgUnitId)),
            Units = units.ToDictionary(u => u.Id, u => u.Name),
            Jobs = jobs.ToDictionary(j => j.Id, j => j.Name),
            Companies = await OrgReferences.CompaniesAsync(organization, companyIds, cancellationToken),
            Branches = branchList.Count == 0
                ? new Dictionary<Guid, BranchSummary>()
                : (await organization.GetBranchesAsync(branchList, cancellationToken)).ToDictionary(b => b.Id),
        };
    }

    public string? PositionCode(PositionId? id) => id is { } key && Positions.TryGetValue(key, out var p) ? p.Code : null;

    public OrgUnitId? UnitOf(PositionId? id) => id is { } key && Positions.TryGetValue(key, out var p) ? p.UnitId : null;

    public string? UnitName(OrgUnitId? id) => id is { } key ? Units.GetValueOrDefault(key) : null;

    public string? JobName(JobId? id) => id is { } key ? Jobs.GetValueOrDefault(key) : null;

    public string CompanyName(Guid id) => Companies.GetValueOrDefault(id)?.ShortName ?? string.Empty;

    public string? BranchName(Guid? id) => id is { } key ? Branches.GetValueOrDefault(key)?.Name : null;
}

internal static class EmployeePhotos
{
    /// <summary>Current photo files of the people; normally one each.</summary>
    public static IQueryable<StoredFile> Files(IHrDbContext db, IReadOnlyCollection<Guid> employeeIds) =>
        db.StoredFiles.Where(f => f.OwnerType == HrFileOwners.Employee && employeeIds.Contains(f.OwnerId) && f.Purpose == StoredFilePurposes.Photo);

    public static async Task<Dictionary<Guid, Guid>> IdsAsync(IHrDbContext db, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken)
    {
        if (employeeIds.Count == 0)
        {
            return [];
        }

        var files = await Files(db, employeeIds).AsNoTracking()
            .OrderBy(f => f.CreatedAt)
            .Select(f => new { f.OwnerId, f.Id })
            .ToListAsync(cancellationToken);

        // The newest one wins if a replaced photo has not been removed yet
        return files.GroupBy(f => f.OwnerId).ToDictionary(g => g.Key, g => g.Last().Id);
    }
}

internal static class EmployeeMasks
{
    public static string Tckn(string value) =>
        SharedKernel.ValueObjects.Tckn.Create(value) is { IsSuccess: true } tckn ? tckn.Value.Masked() : new string('*', value.Length);

    public static string? Iban(string? value) =>
        value is null ? null
        : SharedKernel.ValueObjects.Iban.Create(value) is { IsSuccess: true } iban ? iban.Value.Masked()
        : new string('*', value.Length);
}

internal sealed class GetEmployeesQueryHandler(
    IHrDbContext db,
    IAccessScopeProvider scopes,
    IPositionHolders holders,
    ICurrentUser currentUser,
    ICompanyContext companyContext,
    IOrganizationLookup organization,
    TimeProvider timeProvider) : IQueryHandler<GetEmployeesQuery, PagedResult<EmployeeListItem>>
{
    public async Task<Result<PagedResult<EmployeeListItem>>> Handle(GetEmployeesQuery request, CancellationToken cancellationToken)
    {
        var scope = await EmployeeScope.ResolveAsync(scopes, request.Permission, db, holders, currentUser, cancellationToken);
        var days = await CompanyDays.ResolveAsync(db, organization, timeProvider, cancellationToken);
        var employees = db.Employees.AsNoTracking().WhereCustomFields(CustomFieldFiltering.Parse(request.CustomFields, out _));
        var rows = Filter(scope.Filter(EmploymentRows.Query(employees, days), db), request);
        var page = await GetEmployeesQuery.Sorts.Apply(rows, request.Paging.Sort, GetEmployeesQuery.DefaultSort)
            .ToPagedResultAsync(request.Paging, cancellationToken);

        var items = page.Items;
        var names = await EmployeeNames.LoadAsync(
            db,
            organization,
            items.Where(r => r.PositionId is not null).Select(r => r.PositionId!.Value),
            items.Where(r => r.JobId is not null).Select(r => r.JobId!.Value),
            items.Select(r => r.CompanyId),
            items.Where(r => r.BranchId is not null).Select(r => r.BranchId!.Value),
            cancellationToken);
        var photos = await EmployeePhotos.IdsAsync(db, [.. items.Select(r => r.EmployeeId.Value).Distinct()], cancellationToken);
        return new PagedResult<EmployeeListItem>(
            [.. items.Select(r =>
            {
                var unitId = names.UnitOf(r.PositionId);
                return new EmployeeListItem(
                    r.EmployeeId.Value,
                    r.EmploymentId.Value,
                    r.FirstName,
                    r.LastName,
                    $"{r.FirstName} {r.LastName}",
                    EmployeeMasks.Tckn(r.Tckn),
                    r.CompanyId,
                    names.CompanyName(r.CompanyId),
                    r.RegistryNumber,
                    r.HireDate,
                    r.TerminationDate,
                    r.Status,
                    r.IsPrimary,
                    r.PositionId?.Value,
                    names.PositionCode(r.PositionId),
                    r.JobId?.Value,
                    names.JobName(r.JobId),
                    unitId?.Value,
                    names.UnitName(unitId),
                    r.BranchId,
                    names.BranchName(r.BranchId),
                    photos.TryGetValue(r.EmployeeId.Value, out var photo) ? photo : null,
                    r.CustomFields.Values);
            })],
            page.Page,
            page.PageSize,
            page.TotalCount);

        IQueryable<EmploymentRow> Filter(IQueryable<EmploymentRow> query, GetEmployeesQuery filter)
        {
            foreach (var companyId in new[] { filter.CompanyId, companyContext.ActiveCompanyId }.OfType<Guid>().Distinct())
            {
                query = query.Where(r => r.CompanyId == companyId);
            }

            if (filter.BranchId is { } branchId)
            {
                query = query.Where(r => r.BranchId == branchId);
            }

            if (filter.OrgUnitId is { } orgUnitId)
            {
                query = EmploymentRows.InUnit(query, db, OrgUnitId.From(orgUnitId), filter.IncludeSubUnits);
            }

            if (filter.PositionId is { } positionFilter)
            {
                var positionId = Domain.Organization.PositionId.From(positionFilter);
                query = query.Where(r => r.PositionId == positionId);
            }

            if (filter.JobId is { } jobFilter)
            {
                var jobId = Domain.Organization.JobId.From(jobFilter);
                query = query.Where(r => r.JobId == jobId);
            }

            var statuses = filter.Statuses is { Count: > 0 } given ? given.Distinct().ToList() : [EmploymentStatus.Active];
            query = query.Where(r => statuses.Contains(r.Status));
            return EmploymentRows.Search(query, filter.Search);
        }
    }
}

internal sealed class GetEmployeeQueryHandler(
    IHrDbContext db,
    IAccessScopeProvider scopes,
    IPositionHolders holders,
    ICurrentUser currentUser,
    IOrganizationLookup organization,
    ISensitiveDataProtector protector,
    ISensitiveReadLog readLog,
    IUserDirectory users,
    ITextLocalizer localizer,
    TimeProvider timeProvider) : IQueryHandler<GetEmployeeQuery, EmployeeDetails>
{
    public async Task<Result<EmployeeDetails>> Handle(GetEmployeeQuery request, CancellationToken cancellationToken)
    {
        var id = EmployeeId.From(request.Id);
        var days = await CompanyDays.ResolveAsync(db, organization, timeProvider, cancellationToken);
        var rows = EmploymentRows.Query(db.Employees.AsNoTracking().Where(e => e.Id == id), days);
        var scope = await EmployeeScope.ResolveAsync(scopes, request.Permission, db, holders, currentUser, cancellationToken);
        var visible = await scope.Filter(rows, db).Select(r => r.EmploymentId).ToListAsync(cancellationToken);
        if (visible.Count == 0)
        {
            return CommonErrors.NotFound;
        }

        var employee = await db.Employees.AsNoTracking()
            .Include(e => e.Employments)
            .ThenInclude(m => m.Assignments)
            .SingleAsync(e => e.Id == id, cancellationToken);
        var stamps = await db.Employees.AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new { Version = EF.Property<uint>(e, ConcurrencyVersion.PropertyName), e.CreatedAt, e.ModifiedAt })
            .SingleAsync(cancellationToken);

        var sensitiveScope = await EmployeeScope.ResolveAsync(scopes, HrPermissions.Employees.Sensitive.Read, db, holders, currentUser, cancellationToken);
        var sensitiveVisible = await sensitiveScope.Filter(rows, db).AnyAsync(cancellationToken);

        var employments = employee.Employments.Where(m => visible.Contains(m.Id)).OrderByDescending(m => m.HireDate).ToList();
        var assignments = employments.SelectMany(m => m.Assignments).ToList();
        var names = await EmployeeNames.LoadAsync(
            db,
            organization,
            assignments.Select(a => a.PositionId).Concat(assignments.Where(a => a.ManagerPositionId is not null).Select(a => a.ManagerPositionId!.Value)),
            assignments.Select(a => a.JobId),
            employments.Select(m => m.CompanyId),
            assignments.Where(a => a.BranchId is not null).Select(a => a.BranchId!.Value),
            cancellationToken);
        var exitCodes = await ExitCodeNamesAsync(employments, cancellationToken);
        var photos = await EmployeePhotos.IdsAsync(db, [request.Id], cancellationToken);
        var sensitive = sensitiveVisible ? await ReadSensitiveAsync(employee, cancellationToken) : new EmployeeSensitiveItem(true, null, null, null, null);
        var user = employee.UserId is { } userId
            ? (await users.GetAsync([userId], cancellationToken)).SingleOrDefault() is { } found
                ? new EmployeeUserItem(found.Id, found.UserName, found.Email, found.IsActive)
                : null
            : null;

        return new EmployeeDetails(
            request.Id,
            sensitiveVisible ? employee.Tckn : EmployeeMasks.Tckn(employee.Tckn),
            employee.FirstName,
            employee.LastName,
            employee.FullName,
            employee.BirthDate,
            employee.BirthPlace,
            employee.Gender,
            employee.Nationality,
            employee.MaritalStatus,
            employee.MilitaryStatus,
            sensitiveVisible ? employee.Iban : EmployeeMasks.Iban(employee.Iban),
            sensitiveVisible,
            employee.UserId,
            employee.UserLinkState,
            employee.UserLinkError is { } error ? localizer.Get(error, culture: currentUser.Culture) : null,
            user,
            photos.TryGetValue(request.Id, out var photo) ? photo : null,
            employee.Emails,
            employee.Phones,
            employee.Addresses,
            employee.EmergencyContacts,
            employee.FamilyMembers,
            employee.Educations,
            employee.Certificates,
            employee.Languages,
            employee.CustomFields.Values,
            sensitive,
            [.. employments.Select(m => ToItem(m, days.Of(m.CompanyId, timeProvider), names, exitCodes))],
            stamps.Version,
            stamps.CreatedAt,
            stamps.ModifiedAt);
    }

    private static EmploymentItem ToItem(Employment m, DateOnly today, EmployeeNames names, IReadOnlyDictionary<string, string> exitCodes) =>
        new(
            m.Id.Value,
            m.CompanyId,
            names.CompanyName(m.CompanyId),
            m.RegistryNumber,
            m.HireDate,
            m.SeniorityDate,
            m.SeniorityReason,
            m.Type,
            m.ContractEndDate,
            m.WeeklyHours,
            m.ProbationEndDate,
            m.WorkScheduleId,
            m.LeavePolicyId,
            m.IsPrimary,
            m.StatusOn(today),
            m.TerminationDate,
            m.SgkExitCode,
            m.SgkExitCode is { } code ? exitCodes.GetValueOrDefault(code) : null,
            m.TerminationReason,
            m.Rehireable,
            m.TerminationNote,
            [.. m.Assignments.OrderByDescending(a => a.ValidFrom).Select(a =>
            {
                var unitId = names.UnitOf(a.PositionId);
                return new JobAssignmentItem(
                    a.Id.Value,
                    a.PositionId.Value,
                    names.PositionCode(a.PositionId),
                    unitId?.Value,
                    names.UnitName(unitId),
                    a.JobId.Value,
                    names.JobName(a.JobId),
                    a.BranchId,
                    names.BranchName(a.BranchId),
                    a.ManagerPositionId?.Value,
                    names.PositionCode(a.ManagerPositionId),
                    a.Level,
                    a.CostCenterCode,
                    a.Reason,
                    a.ValidFrom,
                    a.ValidTo,
                    m.StatusOn(today) == EmploymentStatus.Active && a.Covers(today));
            })]);

    private async Task<IReadOnlyDictionary<string, string>> ExitCodeNamesAsync(List<Employment> employments, CancellationToken cancellationToken)
    {
        var codes = employments.Where(m => m.SgkExitCode is not null).Select(m => m.SgkExitCode!).Distinct().ToList();
        if (codes.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        var rows = await db.SgkExitCodes.AsNoTracking().Where(c => codes.Contains(c.Code)).ToListAsync(cancellationToken);
        return rows.ToDictionary(c => c.Code, c => c.Name.Get(currentUser.Culture, "tr-TR"), StringComparer.Ordinal);
    }

    /// <summary>The plain values; the read of the TCKN, the IBAN and every special category value is audited.</summary>
    private async Task<EmployeeSensitiveItem> ReadSensitiveAsync(Employee employee, CancellationToken cancellationToken)
    {
        var entityId = employee.Id.Value.ToString();
        List<string> personFields = [nameof(Employee.Tckn)];
        if (employee.Iban is not null)
        {
            personFields.Add(nameof(Employee.Iban));
        }

        await readLog.RecordAsync(typeof(Employee), entityId, personFields, cancellationToken);

        var data = await db.EmployeeSensitiveData.AsNoTracking().SingleOrDefaultAsync(s => s.Id == employee.Id, cancellationToken);
        if (data is null)
        {
            return new EmployeeSensitiveItem(false, null, null, null, null);
        }

        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [nameof(EmployeeSensitiveData.DisabilityDegree)] = protector.Unprotect(data.DisabilityDegree),
            [nameof(EmployeeSensitiveData.BloodType)] = protector.Unprotect(data.BloodType),
            [nameof(EmployeeSensitiveData.HealthNotes)] = protector.Unprotect(data.HealthNotes),
            [nameof(EmployeeSensitiveData.CriminalRecordDate)] = protector.Unprotect(data.CriminalRecordDate),
        };
        var read = values.Where(v => v.Value is not null).Select(v => v.Key).ToList();
        if (read.Count > 0)
        {
            await readLog.RecordAsync(typeof(EmployeeSensitiveData), entityId, read, cancellationToken);
        }

        return new EmployeeSensitiveItem(
            false,
            int.TryParse(values[nameof(EmployeeSensitiveData.DisabilityDegree)], NumberStyles.None, CultureInfo.InvariantCulture, out var degree) ? degree : null,
            values[nameof(EmployeeSensitiveData.BloodType)],
            values[nameof(EmployeeSensitiveData.HealthNotes)],
            DateOnly.TryParseExact(values[nameof(EmployeeSensitiveData.CriminalRecordDate)], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : null);
    }
}
