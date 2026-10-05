using Merp.Application.Abstractions.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Merp.Web.Security;

// Role assignments arrive with PLAN P2, which replaces both providers below with the real one.

/// <summary>
/// Development only, until P2: a user who holds the permission (<see cref="DevelopmentPermissionChecker"/>) has
/// the scopes listed in <c>Development:Scopes:{userName}</c> as <c>Type</c> or <c>Type:id</c>, e.g.
/// <c>"Company:0199…"</c>. Users that are not listed have the Tenant scope.
/// </summary>
public sealed class DevelopmentAccessScopeProvider(
    IHttpContextAccessor httpContextAccessor,
    IPermissionChecker permissionChecker,
    IConfiguration configuration) : IAccessScopeProvider
{
    public Task<IReadOnlyList<AccessScope>> GetScopesAsync(Guid userId, string permission, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Scopes of other users need role assignments (PLAN P2).");

    public async Task<IReadOnlyList<AccessScope>> GetCurrentUserScopesAsync(string permission, CancellationToken cancellationToken = default)
    {
        if (!await permissionChecker.HasPermissionAsync(permission, cancellationToken))
        {
            return [];
        }

        var userName = httpContextAccessor.HttpContext?.User.FindFirst("preferred_username")?.Value;
        var configured = configuration.GetSection($"Development:Scopes:{userName}").Get<string[]>();
        return configured is { Length: > 0 } ? AccessScopeParser.Parse(configured) : [AccessScope.Tenant];
    }
}

/// <summary>Outside Development, until P2: nobody has any scope.</summary>
public sealed class DenyAllAccessScopeProvider : IAccessScopeProvider
{
    public Task<IReadOnlyList<AccessScope>> GetScopesAsync(Guid userId, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AccessScope>>([]);

    public Task<IReadOnlyList<AccessScope>> GetCurrentUserScopesAsync(string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AccessScope>>([]);
}

public static class AccessScopeParser
{
    /// <summary>Parses <c>Type</c> or <c>Type:id</c> entries; malformed entries are skipped.</summary>
    public static IReadOnlyList<AccessScope> Parse(IEnumerable<string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var scopes = new List<AccessScope>();
        foreach (var entry in entries)
        {
            var parts = entry.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts[0].Length == 0)
            {
                continue;
            }

            if (parts.Length == 1)
            {
                scopes.Add(new AccessScope(parts[0]));
            }
            else if (Guid.TryParse(parts[1], out var id))
            {
                scopes.Add(new AccessScope(parts[0], id));
            }
        }

        return scopes;
    }
}
