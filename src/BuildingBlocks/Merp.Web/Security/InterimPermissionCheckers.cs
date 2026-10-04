using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Security;
using Microsoft.AspNetCore.Http;

namespace Merp.Web.Security;

// Roles and role assignments arrive with PLAN P2, which replaces both checkers below with the real one.

/// <summary>Development only, until P2: every authenticated user of a resolved tenant holds every permission.</summary>
public sealed class DevelopmentPermissionChecker(IHttpContextAccessor httpContextAccessor, ITenantContext tenantContext) : IPermissionChecker
{
    public Task<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult(httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true && tenantContext.HasTenant);
}

/// <summary>Outside Development, until P2: nobody holds any permission.</summary>
public sealed class DenyAllPermissionChecker : IPermissionChecker
{
    public Task<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken = default) => Task.FromResult(false);
}
