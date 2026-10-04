using System.Globalization;
using System.Security.Claims;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Security;
using Merp.Web.Navigation;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace Merp.Modules.Platform.Api.Endpoints;

internal static class MeEndpoints
{
    /// <summary>
    /// The signed-in user, tenant, granted permissions and menu, used by the web shell. Every signed-in user may
    /// read their own data (D22). Companies and preferences become real with PLAN P1 and P2.
    /// </summary>
    public static void MapMeEndpoints(this IEndpointRouteBuilder group) =>
        group.MapGet("/me", GetMeAsync)
            .RequireAuthenticatedUser()
            .WithName("GetMe")
            .WithSummary("Current user, tenant, permissions and menu");

    private static async Task<MeResponse> GetMeAsync(
        HttpContext context,
        ITenantContext tenant,
        ICurrentUser currentUser,
        IPermissionChecker permissionChecker,
        IEnumerable<IPermissionDefinitionProvider> permissionProviders,
        IEnumerable<IMenuContributor> menuContributors,
        IConfiguration configuration)
    {
        var granted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var permission in permissionProviders.SelectMany(p => p.GetPermissions()).Select(p => p.Code).Distinct(StringComparer.Ordinal))
        {
            if (await permissionChecker.HasPermissionAsync(permission, context.RequestAborted))
            {
                granted.Add(permission);
            }
        }

        var menu = Filter(menuContributors.SelectMany(c => c.GetMenuItems()), granted);
        var user = context.User;
        var cultures = (configuration.GetSection("Localization:SupportedCultures").Get<string[]>() ?? ["tr-TR"])
            .Select(c => CultureInfo.GetCultureInfo(c))
            .Select(c => new CultureResponse(c.Name, c.NativeName))
            .ToList();

        return new MeResponse(
            new UserResponse(
                user.FindFirstValue("sub") ?? string.Empty,
                user.FindFirstValue("preferred_username"),
                user.FindFirstValue("name"),
                user.FindFirstValue("email")),
            new TenantResponse(tenant.RequiredTenant.Code, tenant.RequiredTenant.Name),
            [.. granted.Order(StringComparer.Ordinal)],
            [],
            menu,
            new PreferencesResponse(currentUser.Culture),
            cultures);
    }

    // Keeps items the user may open; groups without visible children disappear.
    private static List<MenuItemResponse> Filter(IEnumerable<MenuItem> items, HashSet<string> granted) =>
        [.. items
            .Where(i => i.Permission is null || granted.Contains(i.Permission))
            .OrderBy(i => i.Order)
            .Select(i => new MenuItemResponse(i.Key, i.LabelKey, i.Path, i.Icon, i.Children is null ? null : Filter(i.Children, granted)))
            .Where(i => i.Path is not null || i.Children is { Count: > 0 })];
}

public sealed record MeResponse(
    UserResponse User,
    TenantResponse Tenant,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<CompanyResponse> Companies,
    IReadOnlyList<MenuItemResponse> Menu,
    PreferencesResponse Preferences,
    IReadOnlyList<CultureResponse> Cultures);

public sealed record UserResponse(string Subject, string? UserName, string? DisplayName, string? Email);

public sealed record TenantResponse(string Code, string Name);

public sealed record CompanyResponse(Guid Id, string Code, string Name);

public sealed record MenuItemResponse(string Key, string LabelKey, string? Path, string? Icon, IReadOnlyList<MenuItemResponse>? Children);

public sealed record PreferencesResponse(string Culture);

public sealed record CultureResponse(string Code, string Name);
