using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Merp.Web.Security;

// Role assignments with scopes arrive with PLAN P2, which replaces this provider with the real one. Outside
// Development the default from AddMerpInfrastructure grants no scope.

/// <summary>
/// Development only, until P2: data scopes per Keycloak user name from <c>Development:Scopes</c>, e.g.
/// <c>"ik": ["Company:0199…"]</c>. Users without an entry see the whole tenant.
/// </summary>
public sealed class DevelopmentAccessScopeProvider(IHttpContextAccessor httpContextAccessor, ITenantContext tenantContext, IConfiguration configuration)
    : IAccessScopeProvider
{
    public Task<IReadOnlyList<AccessScope>> GetScopesAsync(Guid userId, string permission, CancellationToken cancellationToken = default)
    {
        var user = httpContextAccessor.HttpContext?.User;
        var userName = user?.FindFirst("preferred_username")?.Value;
        if (user?.Identity?.IsAuthenticated != true || !tenantContext.HasTenant || userName is null)
        {
            return Task.FromResult<IReadOnlyList<AccessScope>>([]);
        }

        var configured = configuration.GetSection($"Development:Scopes:{userName}").Get<string[]>();
        return Task.FromResult(configured is null ? AccessScopeParser.TenantWide : AccessScopeParser.Parse(configured));
    }
}

/// <summary>Parses scopes written as <c>Type</c> or <c>Type:id</c> (e.g. <c>Tenant</c>, <c>Company:0199…</c>).</summary>
public static class AccessScopeParser
{
    public static IReadOnlyList<AccessScope> TenantWide { get; } = [new(AccessScopeTypes.Tenant)];

    public static IReadOnlyList<AccessScope> Parse(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var scopes = new List<AccessScope>();
        foreach (var value in values.Select(v => v.Trim()).Where(v => v.Length > 0))
        {
            var separator = value.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0)
            {
                scopes.Add(new AccessScope(value));
            }
            else if (Guid.TryParse(value[(separator + 1)..], out var id))
            {
                scopes.Add(new AccessScope(value[..separator], id));
            }
        }

        return scopes;
    }
}
