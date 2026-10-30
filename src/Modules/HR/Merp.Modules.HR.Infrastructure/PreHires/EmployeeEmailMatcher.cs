using Merp.Modules.HR.Application.PreHires;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Infrastructure.PreHires;

/// <summary>Finds an employee by one of their e-mail addresses in the jsonb list, ignoring case (D54).</summary>
internal sealed class EmployeeEmailMatcher(HrDbContext db) : IEmployeeEmailMatcher
{
    public async Task<EmployeeId?> FindAsync(string email, CancellationToken cancellationToken)
    {
        var address = email.Trim().ToLowerInvariant();
        var ids = await db.Employees
            .FromSql($"SELECT e.* FROM hr.employees e WHERE EXISTS (SELECT 1 FROM jsonb_array_elements(e.emails) m WHERE lower(m ->> 'address') = {address})")
            .Select(e => e.Id)
            .Take(2)
            .ToListAsync(cancellationToken);

        // Two people with the same address are left to HR
        return ids.Count == 1 ? ids[0] : null;
    }
}
