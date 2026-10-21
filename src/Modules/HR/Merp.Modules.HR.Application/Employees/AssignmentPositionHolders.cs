using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Organization;
using Merp.Modules.Platform.Contracts.Organization;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Employees;

/// <summary>
/// <see cref="IPositionHolders"/> from the job assignments (D38): an employee holds a position on the days their
/// assignment covers while their employment lasts. "Today" is the day in the company's time zone.
/// </summary>
public sealed class AssignmentPositionHolders(IHrDbContext db, IOrganizationLookup organization, TimeProvider timeProvider) : IPositionHolders
{
    public async Task<IReadOnlyList<PositionHolder>> GetHoldersAsync(IReadOnlyCollection<PositionId> positionIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(positionIds);
        if (positionIds.Count == 0)
        {
            return [];
        }

        var ids = positionIds.Distinct().ToList();
        var since = Since();
        var rows = await db.Employees.AsNoTracking()
            .SelectMany(e => e.Employments, (e, m) => new { Employee = e, Employment = m })
            .SelectMany(x => x.Employment.Assignments, (x, a) => new { x.Employee, x.Employment, Assignment = a })
            .Where(x => ids.Contains(x.Assignment.PositionId) && (x.Assignment.ValidTo == null || x.Assignment.ValidTo >= since))
            .Select(x => new HolderRow(
                x.Employee.Id,
                x.Employee.UserId,
                x.Employment.CompanyId,
                x.Employment.HireDate,
                x.Employment.TerminationDate,
                x.Employment.IsPrimary,
                x.Assignment.PositionId,
                x.Assignment.ValidFrom,
                x.Assignment.ValidTo,
                x.Assignment.ManagerPositionId))
            .ToListAsync(cancellationToken);
        return await TodayAsync(rows, cancellationToken);
    }

    public async Task<IReadOnlyList<PositionHolder>> GetPositionsOfEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var id = EmployeeId.From(employeeId);
        var since = Since();
        var rows = await db.Employees.AsNoTracking()
            .Where(e => e.Id == id)
            .SelectMany(e => e.Employments, (e, m) => new { Employee = e, Employment = m })
            .SelectMany(x => x.Employment.Assignments, (x, a) => new { x.Employee, x.Employment, Assignment = a })
            .Where(x => x.Assignment.ValidTo == null || x.Assignment.ValidTo >= since)
            .Select(x => new HolderRow(
                x.Employee.Id,
                x.Employee.UserId,
                x.Employment.CompanyId,
                x.Employment.HireDate,
                x.Employment.TerminationDate,
                x.Employment.IsPrimary,
                x.Assignment.PositionId,
                x.Assignment.ValidFrom,
                x.Assignment.ValidTo,
                x.Assignment.ManagerPositionId))
            .ToListAsync(cancellationToken);
        return await TodayAsync(rows, cancellationToken);
    }

    public async Task<IReadOnlyList<PositionHolder>> GetPositionsOfUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var since = Since();
        var rows = await db.Employees.AsNoTracking()
            .Where(e => e.UserId == userId)
            .SelectMany(e => e.Employments, (e, m) => new { Employee = e, Employment = m })
            .SelectMany(x => x.Employment.Assignments, (x, a) => new { x.Employee, x.Employment, Assignment = a })
            .Where(x => x.Assignment.ValidTo == null || x.Assignment.ValidTo >= since)
            .Select(x => new HolderRow(
                x.Employee.Id,
                x.Employee.UserId,
                x.Employment.CompanyId,
                x.Employment.HireDate,
                x.Employment.TerminationDate,
                x.Employment.IsPrimary,
                x.Assignment.PositionId,
                x.Assignment.ValidFrom,
                x.Assignment.ValidTo,
                x.Assignment.ManagerPositionId))
            .ToListAsync(cancellationToken);
        return await TodayAsync(rows, cancellationToken);
    }

    // A day before today in UTC: no time zone is that far behind, so no assignment of today is left out
    private DateOnly Since() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).AddDays(-1);

    private async Task<IReadOnlyList<PositionHolder>> TodayAsync(List<HolderRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var companies = await organization.GetCompaniesAsync([.. rows.Select(r => r.CompanyId).Distinct()], cancellationToken);
        var zones = companies.ToDictionary(c => c.Id, c => c.TimeZoneId);
        return [.. rows
            .Where(r =>
            {
                var today = HrClock.Today(timeProvider, zones.GetValueOrDefault(r.CompanyId));
                return r.HireDate <= today
                    && (r.TerminationDate is null || today <= r.TerminationDate)
                    && r.ValidFrom <= today
                    && (r.ValidTo is null || today <= r.ValidTo);
            })
            .OrderByDescending(r => r.IsPrimary)
            .Select(r => new PositionHolder(r.PositionId, r.EmployeeId.Value, r.UserId, r.IsPrimary, r.ManagerPositionId))];
    }

    private sealed record HolderRow(
        EmployeeId EmployeeId,
        Guid? UserId,
        Guid CompanyId,
        DateOnly HireDate,
        DateOnly? TerminationDate,
        bool IsPrimary,
        PositionId PositionId,
        DateOnly ValidFrom,
        DateOnly? ValidTo,
        PositionId? ManagerPositionId);
}
