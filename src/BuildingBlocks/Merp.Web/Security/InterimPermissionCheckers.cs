using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Merp.Web.Security;

// Roles and role assignments arrive with PLAN P2, which replaces both checkers below with the real one.

/// <summary>
/// Development only, until P2: permissions per Keycloak user name from <c>Development:Permissions</c>, e.g.
/// <c>"admin": ["*"]</c>, <c>"ik": ["hr.*"]</c>. Users that are not listed hold no permission.
/// </summary>
public sealed class DevelopmentPermissionChecker(IHttpContextAccessor httpContextAccessor, ITenantContext tenantContext, IConfiguration configuration)
    : IPermissionChecker
{
    public Task<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken = default)
    {
        var user = httpContextAccessor.HttpContext?.User;
        var userName = user?.FindFirst("preferred_username")?.Value;
        if (user?.Identity?.IsAuthenticated != true || !tenantContext.HasTenant || userName is null)
        {
            return Task.FromResult(false);
        }

        var granted = configuration.GetSection($"Development:Permissions:{userName}").Get<string[]>() ?? [];
        return Task.FromResult(granted.Any(pattern => Matches(pattern, permission)));
    }

    internal static bool Matches(string pattern, string permission) =>
        pattern == "*"
        || string.Equals(pattern, permission, StringComparison.Ordinal)
        || (pattern.EndsWith(".*", StringComparison.Ordinal) && permission.StartsWith(pattern[..^1], StringComparison.Ordinal));
}

/// <summary>Outside Development, until P2: nobody holds any permission.</summary>
public sealed class DenyAllPermissionChecker : IPermissionChecker
{
    public Task<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken = default) => Task.FromResult(false);
}
