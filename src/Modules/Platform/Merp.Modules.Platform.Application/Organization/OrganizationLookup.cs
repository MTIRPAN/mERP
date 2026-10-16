using Merp.Modules.Platform.Contracts.Organization;
using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Domain.Companies;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Organization;

/// <summary><see cref="IOrganizationLookup"/> over the Platform tables (D37).</summary>
public sealed class OrganizationLookup(IPlatformDbContext db) : IOrganizationLookup
{
    public async Task<IReadOnlyList<CompanySummary>> GetCompaniesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return [];
        }

        var companyIds = ids.Distinct().Select(CompanyId.From).ToList();
        return await db.Companies.AsNoTracking()
            .Where(c => companyIds.Contains(c.Id))
            .Select(c => new CompanySummary(c.Id.Value, c.Code, c.ShortName, c.TimeZoneId, c.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BranchSummary>> GetBranchesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return [];
        }

        var branchIds = ids.Distinct().Select(BranchId.From).ToList();
        return await db.Branches.AsNoTracking()
            .Where(b => branchIds.Contains(b.Id))
            .Select(b => new BranchSummary(b.Id.Value, b.CompanyId.Value, b.Code, b.Name, b.IsActive))
            .ToListAsync(cancellationToken);
    }
}
