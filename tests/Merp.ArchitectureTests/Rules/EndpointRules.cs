namespace Merp.ArchitectureTests.Rules;

/// <summary>An endpoint as the rules see it.</summary>
public sealed record EndpointInfo(string Route, bool AllowsAnonymous, bool RequiresAuthorization, IReadOnlyList<string> Permissions, bool AuthenticatedUserOnly = false);

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

    /// <summary>
    /// docs/DECISIONS.md D22 (proposal): the caller's own data and public reference data; a signed-in user is enough.
    /// Files follow the permissions of their record through its IFileAccessPolicy (D31).
    /// </summary>
    public static readonly string[] AuthenticatedUserRoutes =
    [
        "/api/platform/me",
        "/api/platform/me/preferences",
        "/api/platform/custom-fields/form",
        "/api/platform/attachments",
        "/api/platform/attachments/{id:guid}",
        "/api/platform/files/{id:guid}",
        "/api/platform/notifications",
        "/api/platform/notifications/unread-count",
        "/api/platform/notifications/{id:guid}/read",
        "/api/platform/notifications/read-all",
        "/api/platform/notifications/preferences",
        "/api/platform/notifications/test",
        "/api/platform/locations/provinces",
        "/api/platform/locations/provinces/{provinceCode:int}/districts",
        "/api/platform/approvals/document-types",
        "/api/platform/approvals/preview",
        "/api/platform/approvals/my-tasks",
        "/api/platform/approvals/my-tasks/count",
        "/api/platform/approvals/tasks/{id:guid}/decision",
        "/api/platform/approvals/requests/{id:guid}",
        "/api/platform/approvals/requests/{id:guid}/summary",
        "/api/platform/approvals/timeline",
        "/api/platform/approvals/delegations",
        "/api/platform/approvals/delegations/{id:guid}/revoke",
        "/api/platform/approvals/users",
        "/api/platform/imports",
        "/api/platform/imports/{key}",
        "/api/platform/imports/{key}/template",
        "/api/platform/imports/{key}/inspect",
        "/api/platform/imports/{key}/mapping-suggestion",
        "/api/platform/imports/jobs",
        "/api/platform/imports/jobs/{id:guid}",
        "/api/platform/imports/jobs/{id:guid}/report",
        "/api/platform/imports/jobs/{id:guid}/commit",
        "/api/platform/assistant/ask",
    ];

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

        if (endpoint.AuthenticatedUserOnly)
        {
            return endpoint.RequiresAuthorization && AuthenticatedUserRoutes.Contains(endpoint.Route, StringComparer.Ordinal)
                ? null
                : $"{endpoint.Route}: RequireAuthenticatedUser() is only allowed for the D22 routes";
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
