using Merp.Application.Abstractions.Files;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Application.Organization;
using Merp.Modules.Platform.Domain.Companies;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Files;

/// <summary>Platform records that carry files (D31).</summary>
public static class PlatformFileOwners
{
    public const string Company = "platform.company";

    /// <summary>Owner types of this module start with <c>platform.</c>; their files are in the <c>platform</c> schema.</summary>
    public static bool IsPlatformOwner(string? ownerType) =>
        ownerType is not null && ownerType.StartsWith(PlatformPermissions.Module + ".", StringComparison.Ordinal);
}

/// <summary>
/// Company files follow the company's permissions and data scope: <c>platform.companies.read</c> lists and downloads,
/// <c>platform.companies.manage</c> also uploads and deletes. A company outside the scope has no files (404).
/// </summary>
public sealed class CompanyFileAccessPolicy(IPlatformDbContext db, IAccessScopeProvider scopes) : IFileAccessPolicy
{
    public string OwnerType => PlatformFileOwners.Company;

    public async Task<FileOwnerAccess> GetAccessAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        var id = CompanyId.From(ownerId);
        var read = OrganizationScope.From(await scopes.GetCurrentUserScopesAsync(PlatformPermissions.Companies.Read, cancellationToken));
        var visible = await (await read.FilterCompaniesAsync(db, cancellationToken)).AnyAsync(c => c.Id == id, cancellationToken);
        if (!visible)
        {
            return FileOwnerAccess.None;
        }

        var manage = OrganizationScope.From(await scopes.GetCurrentUserScopesAsync(PlatformPermissions.Companies.Manage, cancellationToken));
        return manage.CoversCompany(id) ? FileOwnerAccess.Manage : FileOwnerAccess.Read;
    }
}

internal static class PlatformFileAccess
{
    /// <summary>Access to the files of a Platform record; owner types of other modules are not found here.</summary>
    public static Task<FileOwnerAccess> GetAsync(FileAccessPolicies policies, string? ownerType, Guid ownerId, CancellationToken cancellationToken) =>
        PlatformFileOwners.IsPlatformOwner(ownerType)
            ? policies.GetAccessAsync(ownerType, ownerId, cancellationToken)
            : Task.FromResult(FileOwnerAccess.None);

    /// <summary>Manage → success; read only → 403; no access → 404 (the record does not exist for the caller).</summary>
    public static Result RequireManage(FileOwnerAccess access) => access switch
    {
        FileOwnerAccess.Manage => Result.Success(),
        FileOwnerAccess.Read => CommonErrors.Forbidden,
        _ => CommonErrors.NotFound,
    };
}
