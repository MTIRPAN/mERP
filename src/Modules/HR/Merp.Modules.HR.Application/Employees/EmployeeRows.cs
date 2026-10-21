using Merp.Application.Persistence;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Organization;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Employees;

/// <summary>
/// One employment of a person with its state and assignment on the company's day, the shape that employee lists
/// filter, sort and page (D38). The assignment is the one covering the day; for an employment that has not started
/// it is the first one, for one that ended the last one, so lists and scopes still place the person.
/// </summary>
internal sealed class EmploymentRow
{
    public EmployeeId EmployeeId { get; init; }

    public Guid? UserId { get; init; }

    public string FirstName { get; init; } = null!;

    public string LastName { get; init; } = null!;

    public string Tckn { get; init; } = null!;

    public CustomFieldValues CustomFields { get; init; } = CustomFieldValues.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public EmploymentId EmploymentId { get; init; }

    public Guid CompanyId { get; init; }

    public string RegistryNumber { get; init; } = null!;

    public DateOnly HireDate { get; init; }

    public DateOnly? TerminationDate { get; init; }

    public bool IsPrimary { get; init; }

    /// <summary>The company's calendar day the row was computed for.</summary>
    public DateOnly Today { get; init; }

    public EmploymentStatus Status { get; init; }

    public PositionId? PositionId { get; init; }

    public JobId? JobId { get; init; }

    public Guid? BranchId { get; init; }

    public PositionId? ManagerPositionId { get; init; }
}

/// <summary>
/// The calendar day of each company that has employees: day boundaries follow the company's time zone (D10). At any
/// moment a company is on the UTC day or one day behind or ahead of it, so SQL picks the day from three values.
/// </summary>
internal sealed class CompanyDays
{
    private CompanyDays(DateOnly utc, IReadOnlyList<Guid> behindIds, IReadOnlyList<Guid> aheadIds, IReadOnlyDictionary<Guid, DateOnly> days)
    {
        Utc = utc;
        BehindIds = behindIds;
        AheadIds = aheadIds;
        Days = days;
    }

    public DateOnly Utc { get; }

    /// <summary>Companies whose day is the day before the UTC day.</summary>
    public IReadOnlyList<Guid> BehindIds { get; }

    /// <summary>Companies whose day is the day after the UTC day.</summary>
    public IReadOnlyList<Guid> AheadIds { get; }

    private IReadOnlyDictionary<Guid, DateOnly> Days { get; }

    public static async Task<CompanyDays> ResolveAsync(
        IHrDbContext db,
        IOrganizationLookup organization,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var companyIds = await db.Employees.AsNoTracking()
            .SelectMany(e => e.Employments)
            .Select(m => m.CompanyId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var zones = (await organization.GetCompaniesAsync(companyIds, cancellationToken)).ToDictionary(c => c.Id, c => c.TimeZoneId);
        var utc = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var days = companyIds.ToDictionary(id => id, id => HrClock.Today(timeProvider, zones.GetValueOrDefault(id)));
        return new CompanyDays(
            utc,
            [.. days.Where(d => d.Value < utc).Select(d => d.Key)],
            [.. days.Where(d => d.Value > utc).Select(d => d.Key)],
            days);
    }

    /// <summary>The company's day; a company without employees yet is on the default time zone's day.</summary>
    public DateOnly Of(Guid companyId, TimeProvider timeProvider) =>
        Days.TryGetValue(companyId, out var day) ? day : HrClock.Today(timeProvider, null);
}

internal static class EmploymentRows
{
    /// <summary>Every employment of <paramref name="employees"/> as a row on its company's day.</summary>
    public static IQueryable<EmploymentRow> Query(IQueryable<Employee> employees, CompanyDays days)
    {
        ArgumentNullException.ThrowIfNull(days);
        var utc = days.Utc;
        var behind = utc.AddDays(-1);
        var ahead = utc.AddDays(1);
        var behindIds = days.BehindIds;
        var aheadIds = days.AheadIds;
        return employees
            .SelectMany(e => e.Employments, (e, m) => new
            {
                Employee = e,
                Employment = m,
                Today = behindIds.Contains(m.CompanyId) ? behind : aheadIds.Contains(m.CompanyId) ? ahead : utc,
            })
            .Select(x => new
            {
                x.Employee,
                x.Employment,
                x.Today,
                Assignment = x.Employment.Assignments
                    .Where(a => a.ValidFrom <= x.Today || a.ValidFrom == x.Employment.HireDate)
                    .OrderByDescending(a => a.ValidFrom)
                    .FirstOrDefault(),
            })
            .Select(x => new EmploymentRow
            {
                EmployeeId = x.Employee.Id,
                UserId = x.Employee.UserId,
                FirstName = x.Employee.FirstName,
                LastName = x.Employee.LastName,
                Tckn = x.Employee.Tckn,
                CustomFields = x.Employee.CustomFields,
                CreatedAt = x.Employee.CreatedAt,
                EmploymentId = x.Employment.Id,
                CompanyId = x.Employment.CompanyId,
                RegistryNumber = x.Employment.RegistryNumber,
                HireDate = x.Employment.HireDate,
                TerminationDate = x.Employment.TerminationDate,
                IsPrimary = x.Employment.IsPrimary,
                Today = x.Today,
                Status = x.Today < x.Employment.HireDate
                    ? EmploymentStatus.Upcoming
                    : x.Employment.TerminationDate != null && x.Employment.TerminationDate < x.Today
                        ? EmploymentStatus.Terminated
                        : EmploymentStatus.Active,
                PositionId = x.Assignment == null ? null : x.Assignment.PositionId,
                JobId = x.Assignment == null ? null : x.Assignment.JobId,
                BranchId = x.Assignment == null ? null : x.Assignment.BranchId,
                ManagerPositionId = x.Assignment == null ? null : x.Assignment.ManagerPositionId,
            });
    }

    /// <summary>Rows whose assignment is in the unit, or with <paramref name="includeSubUnits"/> in its subtree.</summary>
    public static IQueryable<EmploymentRow> InUnit(IQueryable<EmploymentRow> rows, IHrDbContext db, OrgUnitId unitId, bool includeSubUnits)
    {
        var positions = db.Positions;
        if (!includeSubUnits)
        {
            return rows.Where(r => positions.Any(p => p.Id == r.PositionId && p.OrgUnitId == unitId));
        }

        var units = db.OrgUnits;
        return rows.Where(r => positions.Any(p => p.Id == r.PositionId
            && units.Any(u => u.Id == p.OrgUnitId && units.Any(root => root.Id == unitId && OrgPaths.IsWithin(u.Path, root.Path)))));
    }

    /// <summary>
    /// Part of the first name, last name, full name or registry number, or a whole TCKN. A whole TCKN finds the person a
    /// hire was refused for (D38); hiring already tells whether it is registered, so this reveals nothing more.
    /// </summary>
    public static IQueryable<EmploymentRow> Search(IQueryable<EmploymentRow> rows, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return rows;
        }

        var term = search.Trim();
        if (SharedKernel.ValueObjects.Tckn.Create(term) is { IsSuccess: true } tckn)
        {
            var value = tckn.Value.Value;
            return rows.Where(r => r.Tckn == value);
        }

        var pattern = TextSearch.ContainsPattern(search);
        return rows.Where(r =>
            TextSearch.Matches(r.FirstName, pattern)
            || TextSearch.Matches(r.LastName, pattern)
            || TextSearch.Matches(r.FirstName + " " + r.LastName, pattern)
            || TextSearch.Matches(r.RegistryNumber, pattern));
    }
}
