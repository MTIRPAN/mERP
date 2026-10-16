namespace Merp.Modules.Platform.Contracts.Organization;

/// <param name="Id">The company.</param>
/// <param name="Code">Company code; it never changes.</param>
/// <param name="ShortName">Name shown in lists.</param>
/// <param name="TimeZoneId">IANA time zone of the company's day boundaries.</param>
/// <param name="IsActive">Inactive companies get no new records.</param>
public sealed record CompanySummary(Guid Id, string Code, string ShortName, string TimeZoneId, bool IsActive);

/// <param name="Id">The branch.</param>
/// <param name="CompanyId">The branch's company.</param>
/// <param name="Code">Branch code, unique within the company.</param>
/// <param name="Name">Branch name.</param>
/// <param name="IsActive">Inactive branches get no new records.</param>
public sealed record BranchSummary(Guid Id, Guid CompanyId, string Code, string Name, bool IsActive);

/// <summary>
/// Companies and branches for other modules (D37): to check the ids their records point to and to show the names.
/// Read only; scopes are not applied, the calling module applies its own.
/// </summary>
public interface IOrganizationLookup
{
    /// <summary>The companies among <paramref name="ids"/> that exist.</summary>
    Task<IReadOnlyList<CompanySummary>> GetCompaniesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>The branches among <paramref name="ids"/> that exist.</summary>
    Task<IReadOnlyList<BranchSummary>> GetBranchesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
