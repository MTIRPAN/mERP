using Merp.Application.Persistence;
using Merp.Modules.HR.Contracts.Employees;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Employees;

/// <summary>People of a company for other modules (D48): names and registry numbers only.</summary>
public sealed class EmployeeLookup(IHrDbContext db) : IEmployeeLookup
{
    public const int MaxResults = 50;

    public async Task<IReadOnlyList<EmployeeSummary>> SearchAsync(Guid companyId, string? search, int take, CancellationToken cancellationToken = default)
    {
        var rows = Rows(companyId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = TextSearch.ContainsPattern(search);
            rows = rows.Where(r => TextSearch.Matches(r.FullName, pattern) || TextSearch.Matches(r.RegistryNumber, pattern));
        }

        return await rows
            .OrderBy(r => r.TerminationDate != null)
            .ThenBy(r => r.FullName)
            .Take(Math.Clamp(take, 1, MaxResults))
            .ToListAsync(cancellationToken);
    }

    public Task<EmployeeSummary?> FindAsync(Guid employeeId, Guid companyId, CancellationToken cancellationToken = default) =>
        Rows(companyId)
            .Where(r => r.EmployeeId == employeeId)
            .OrderBy(r => r.TerminationDate != null)
            .FirstOrDefaultAsync(cancellationToken);

    private IQueryable<EmployeeSummary> Rows(Guid companyId) =>
        db.Employees.AsNoTracking()
            .SelectMany(e => e.Employments, (e, m) => new { Employee = e, Employment = m })
            .Where(x => x.Employment.CompanyId == companyId)
            .Select(x => new EmployeeSummary(
                x.Employee.Id.Value,
                x.Employment.Id.Value,
                x.Employee.FirstName + " " + x.Employee.LastName,
                x.Employment.RegistryNumber,
                x.Employment.CompanyId,
                x.Employment.TerminationDate));
}
