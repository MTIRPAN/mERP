using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;

namespace Merp.Modules.HR.Application.Organization;

/// <summary>Checks of the Platform records (company, branch) that HR records point to (D37).</summary>
internal static class OrgReferences
{
    /// <summary>New records go to an existing, active company.</summary>
    public static async Task<FieldError?> CheckCompanyAsync(IOrganizationLookup organization, Guid companyId, CancellationToken cancellationToken)
    {
        var company = (await organization.GetCompaniesAsync([companyId], cancellationToken)).SingleOrDefault();
        return company switch
        {
            null => new FieldError("companyId", OrgErrors.UnknownCompanyCode),
            { IsActive: false } => new FieldError("companyId", OrgErrors.CompanyInactiveCode),
            _ => null,
        };
    }

    /// <summary>A newly chosen branch exists, is active and belongs to the company.</summary>
    public static async Task<FieldError?> CheckBranchAsync(IOrganizationLookup organization, Guid? branchId, Guid companyId, CancellationToken cancellationToken)
    {
        if (branchId is not { } id)
        {
            return null;
        }

        var branch = (await organization.GetBranchesAsync([id], cancellationToken)).SingleOrDefault();
        return branch is { IsActive: true } && branch.CompanyId == companyId ? null : new FieldError("branchId", OrgErrors.UnknownBranchCode);
    }

    /// <summary>Short names of the companies, for lists.</summary>
    public static async Task<Dictionary<Guid, CompanySummary>> CompaniesAsync(
        IOrganizationLookup organization,
        IEnumerable<Guid> companyIds,
        CancellationToken cancellationToken) =>
        (await organization.GetCompaniesAsync([.. companyIds.Distinct()], cancellationToken)).ToDictionary(c => c.Id);
}
