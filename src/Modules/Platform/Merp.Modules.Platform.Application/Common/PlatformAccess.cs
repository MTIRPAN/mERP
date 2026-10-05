using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Domain.Companies;

namespace Merp.Modules.Platform.Application.Common;

/// <summary>
/// The companies and branches a caller may see or change with a permission (docs/domain/yetki-ve-onay.md §1.3).
/// Tenant scope covers everything; company scope covers the company and its branches; branch scope covers the
/// branch and, for reading, its company.
/// </summary>
internal sealed record CompanyAccess(bool All, IReadOnlyList<CompanyId> CompanyIds, IReadOnlyList<BranchId> BranchIds)
{
    public bool CoversCompany(CompanyId id) => All || CompanyIds.Contains(id);

    public bool CoversBranch(Branch branch) => All || CompanyIds.Contains(branch.CompanyId) || BranchIds.Contains(branch.Id);

    public IQueryable<Company> Companies(IPlatformDbContext db, bool includeParentsOfBranches)
    {
        if (All)
        {
            return db.Companies;
        }

        var companyIds = CompanyIds;
        if (!includeParentsOfBranches || BranchIds.Count == 0)
        {
            return db.Companies.Where(c => companyIds.Contains(c.Id));
        }

        var branchIds = BranchIds;
        var parents = db.Branches.Where(b => branchIds.Contains(b.Id)).Select(b => b.CompanyId);
        return db.Companies.Where(c => companyIds.Contains(c.Id) || parents.Contains(c.Id));
    }

    public IQueryable<Branch> Branches(IPlatformDbContext db)
    {
        if (All)
        {
            return db.Branches;
        }

        var companyIds = CompanyIds;
        var branchIds = BranchIds;
        return db.Branches.Where(b => companyIds.Contains(b.CompanyId) || branchIds.Contains(b.Id));
    }
}

internal sealed class PlatformAccess(IAccessScopeProvider scopes, ICurrentUser currentUser)
{
    public async Task<CompanyAccess> GetAsync(string permission, CancellationToken cancellationToken) =>
        Combine(await scopes.GetScopesAsync(currentUser, permission, cancellationToken));

    /// <summary>Union of the scopes of several permissions, e.g. for the company selector.</summary>
    public async Task<CompanyAccess> GetAnyAsync(IEnumerable<string> permissions, CancellationToken cancellationToken)
    {
        var all = new List<AccessScope>();
        foreach (var permission in permissions)
        {
            all.AddRange(await scopes.GetScopesAsync(currentUser, permission, cancellationToken));
        }

        return Combine(all);
    }

    private static CompanyAccess Combine(IEnumerable<AccessScope> scopes)
    {
        var list = scopes.ToList();
        return new CompanyAccess(
            list.Exists(s => string.Equals(s.Type, AccessScopeTypes.Tenant, StringComparison.Ordinal)),
            [.. list.Where(s => string.Equals(s.Type, AccessScopeTypes.Company, StringComparison.Ordinal) && s.ScopeId is not null)
                .Select(s => CompanyId.From(s.ScopeId!.Value)).Distinct()],
            [.. list.Where(s => string.Equals(s.Type, AccessScopeTypes.Branch, StringComparison.Ordinal) && s.ScopeId is not null)
                .Select(s => BranchId.From(s.ScopeId!.Value)).Distinct()]);
    }
}
