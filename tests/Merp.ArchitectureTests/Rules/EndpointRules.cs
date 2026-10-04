namespace Merp.ArchitectureTests.Rules;

/// <summary>An endpoint as the rules see it.</summary>
public sealed record EndpointInfo(string Route, bool AllowsAnonymous, bool RequiresAuthorization, IReadOnlyList<string> Permissions);

/// <summary>
/// CLAUDE.md rule 6: every endpoint requires a permission. Anonymous endpoints must be on the D20 list; host
/// infrastructure endpoints follow D22.
/// </summary>
public static class EndpointRules
{
    /// <summary>docs/DECISIONS.md D20.</summary>
    public static readonly string[] AnonymousAllowList =
    [
        "/health/live",
        "/health/ready",
        "/api/public/{tenantCode}/careers",
        "/api/public/{tenantCode}/careers/**",
        "/api/public/{tenantCode}/consents/{token}",
        "/api/public/{tenantCode}/offers/{token}",
        "/api/platform/localization/bundles/{culture}",
    ];

    /// <summary>docs/DECISIONS.md D22 (proposal): host infrastructure with its own protection.</summary>
    public static readonly string[] InfrastructurePrefixes = ["/openapi/", "/scalar", "/admin/jobs", "/hubs/notifications"];

    public static IReadOnlyList<string> Violations(IEnumerable<EndpointInfo> endpoints) =>
        [.. endpoints.Select(Check).OfType<string>()];

    private static string? Check(EndpointInfo endpoint)
    {
        if (InfrastructurePrefixes.Any(p => endpoint.Route.StartsWith(p, StringComparison.Ordinal)))
        {
            return null;
        }

        if (endpoint.AllowsAnonymous)
        {
            return IsAllowListed(endpoint.Route) ? null : $"{endpoint.Route}: anonymous but not on the D20 list";
        }

        return endpoint.RequiresAuthorization && endpoint.Permissions.Count > 0
            ? null
            : $"{endpoint.Route}: requires no permission (use .RequirePermission(...))";
    }

    private static bool IsAllowListed(string route) =>
        AnonymousAllowList.Any(allowed => allowed.EndsWith("/**", StringComparison.Ordinal)
            ? route.StartsWith(allowed[..^3] + "/", StringComparison.Ordinal)
            : string.Equals(route, allowed, StringComparison.Ordinal));
}
