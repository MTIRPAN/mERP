using Merp.Application.Persistence;
using Merp.Modules.HR.Contracts.Employees;
using Merp.Modules.HR.Domain.Employees;
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

        return await Summaries(rows
                .OrderBy(r => r.TerminationDate != null)
                .ThenBy(r => r.FullName)
                .Take(Math.Clamp(take, 1, MaxResults)))
            .ToListAsync(cancellationToken);
    }

    public Task<EmployeeSummary?> FindAsync(Guid employeeId, Guid companyId, CancellationToken cancellationToken = default)
    {
        var id = EmployeeId.From(employeeId);
        return Summaries(Rows(companyId).Where(r => r.EmployeeId == id).OrderBy(r => r.TerminationDate != null))
            .FirstOrDefaultAsync(cancellationToken);
    }

    // Filters and sorts run on these rows: EF cannot translate the members of a record built by its constructor
    private IQueryable<Row> Rows(Guid companyId) =>
        db.Employees.AsNoTracking()
            .SelectMany(e => e.Employments, (e, m) => new { Employee = e, Employment = m })
            .Where(x => x.Employment.CompanyId == companyId)
            .Select(x => new Row
            {
                EmployeeId = x.Employee.Id,
                EmploymentId = x.Employment.Id.Value,
                FullName = x.Employee.FirstName + " " + x.Employee.LastName,
                RegistryNumber = x.Employment.RegistryNumber,
                CompanyId = x.Employment.CompanyId,
                TerminationDate = x.Employment.TerminationDate,
            });

    private static IQueryable<EmployeeSummary> Summaries(IQueryable<Row> rows) =>
        rows.Select(r => new EmployeeSummary(r.EmployeeId.Value, r.EmploymentId, r.FullName, r.RegistryNumber, r.CompanyId, r.TerminationDate));

    private sealed class Row
    {
        public EmployeeId EmployeeId { get; init; }

        public Guid EmploymentId { get; init; }

        public string FullName { get; init; } = null!;

        public string RegistryNumber { get; init; } = null!;

        public Guid CompanyId { get; init; }

        public DateOnly? TerminationDate { get; init; }
    }
}
