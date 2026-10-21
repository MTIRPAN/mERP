using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Contracts.Access;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Access;

/// <inheritdoc cref="IPermissionHolders"/>
public sealed class PermissionHolderLookup(IPlatformDbContext db, TimeProvider timeProvider) : IPermissionHolders
{
    public async Task<IReadOnlyList<Guid>> GetUserIdsAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        var today = PlatformClock.Today(timeProvider);
        var roles = db.Roles.Where(r => r.Permissions.Any(p => p.PermissionCode == permission)).Select(r => r.Id);
        return await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Assignments.Any(a =>
                roles.Contains(a.RoleId)
                && (a.ScopeType == AccessScopeTypes.Tenant || (a.ScopeType == AccessScopeTypes.Company && a.ScopeId == companyId))
                && (a.ValidFrom == null || a.ValidFrom <= today)
                && (a.ValidTo == null || a.ValidTo >= today)))
            .OrderBy(u => u.UserName)
            .Select(u => u.Id.Value)
            .ToListAsync(cancellationToken);
    }
}
