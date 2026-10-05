using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Domain.Companies;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Organization;

/// <summary>
/// The companies and branches a caller reaches through the scopes of one permission
/// (docs/domain/yetki-ve-onay.md §1.2). Scope types of other modules (OrgUnit, ManagedTeam, Self) do not reach
/// companies yet; P2 resolves them through the scope type registry.
/// </summary>
public sealed class OrganizationScope
{
    private OrganizationScope(bool tenantWide, IReadOnlyList<CompanyId> companyIds, IReadOnlyList<BranchId> branchIds)
    {
        IsTenantWide = tenantWide;
        CompanyIds = companyIds;
        BranchIds = branchIds;
    }

    /// <summary>Tenant scope: every company and branch.</summary>
    public bool IsTenantWide { get; }

    /// <summary>Company scopes: the company and all of its branches.</summary>
    public IReadOnlyList<CompanyId> CompanyIds { get; }

    /// <summary>Branch scopes: only these branches.</summary>
    public IReadOnlyList<BranchId> BranchIds { get; }

    public bool IsEmpty => !IsTenantWide && CompanyIds.Count == 0 && BranchIds.Count == 0;

    public static OrganizationScope From(IEnumerable<AccessScope> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        var list = scopes.ToList();
        return new OrganizationScope(
            list.Any(s => s.Type == AccessScopeTypes.Tenant),
            [.. IdsOf(list, AccessScopeTypes.Company).Select(CompanyId.From)],
            [.. IdsOf(list, AccessScopeTypes.Branch).Select(BranchId.From)]);
    }

    public bool CoversCompany(CompanyId companyId) => IsTenantWide || CompanyIds.Contains(companyId);

    public bool CoversBranch(Branch branch)
    {
        ArgumentNullException.ThrowIfNull(branch);
        return CoversCompany(branch.CompanyId) || BranchIds.Contains(branch.Id);
    }

    /// <summary>Branches of the scope: all, those of the scoped companies, and the scoped branches.</summary>
    public IQueryable<Branch> FilterBranches(IQueryable<Branch> branches)
    {
        ArgumentNullException.ThrowIfNull(branches);
        if (IsTenantWide)
        {
            return branches;
        }

        var companyIds = CompanyIds;
        var branchIds = BranchIds;
        return branches.Where(b => companyIds.Contains(b.CompanyId) || branchIds.Contains(b.Id));
    }

    /// <summary>Companies a reader sees: the scoped companies and the companies of the scoped branches.</summary>
    public async Task<IQueryable<Company>> FilterCompaniesAsync(IPlatformDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (IsTenantWide)
        {
            return db.Companies;
        }

        var branchIds = BranchIds;
        var viaBranches = branchIds.Count == 0
            ? []
            : await db.Branches.Where(b => branchIds.Contains(b.Id)).Select(b => b.CompanyId).Distinct().ToListAsync(cancellationToken);
        var companyIds = CompanyIds.Concat(viaBranches).Distinct().ToList();
        return db.Companies.Where(c => companyIds.Contains(c.Id));
    }

    private static IEnumerable<Guid> IdsOf(List<AccessScope> scopes, string type) =>
        scopes.Where(s => s.Type == type && s.ScopeId is not null).Select(s => s.ScopeId!.Value).Distinct();
}
