using System.Globalization;
using System.Security.Claims;
using Merp.Application.Abstractions.AI;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Messaging;
using Merp.Modules.Platform.Application.Companies;
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
    /// read their own data (D22). Companies are those the user's data scopes reach (D4); preferences become real with
    /// PLAN P2.
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
        IEnumerable<IAiFeatureDefinitionProvider> aiFeatureProviders,
        IAiService ai,
        ISender sender,
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

        var menu = Filter(menuContributors.SelectMany(c => c.GetMenuItems()), granted, ai);
        var accessible = await sender.Send(new GetAccessibleCompaniesQuery(granted), context.RequestAborted);
        var companies = accessible.IsSuccess
            ? [.. accessible.Value.Select(c => new CompanyResponse(c.Id, c.Code, c.Name))]
            : new List<CompanyResponse>();
        var user = context.User;
        var cultures = (configuration.GetSection("Localization:SupportedCultures").Get<string[]>() ?? ["tr-TR"])
            .Select(c => CultureInfo.GetCultureInfo(c))
            .Select(c => new CultureResponse(c.Name, c.NativeName))
            .ToList();
        var aiFeatures = aiFeatureProviders
            .SelectMany(p => p.GetFeatures())
            .Select(f => f.Key)
            .Distinct(StringComparer.Ordinal)
            .Where(ai.IsEnabled)
            .Order(StringComparer.Ordinal)
            .ToList();

        return new MeResponse(
            new UserResponse(
                user.FindFirstValue("sub") ?? string.Empty,
                user.FindFirstValue("preferred_username"),
                user.FindFirstValue("name"),
                user.FindFirstValue("email")),
            new TenantResponse(tenant.RequiredTenant.Code, tenant.RequiredTenant.Name),
            [.. granted.Order(StringComparer.Ordinal)],
            companies,
            menu,
            new PreferencesResponse(currentUser.Culture),
            cultures,
            new MeAiResponse(aiFeatures.Count > 0, aiFeatures));
    }

    // Keeps items the user may open; groups without visible children disappear.
    private static List<MenuItemResponse> Filter(IEnumerable<MenuItem> items, HashSet<string> granted, IAiService ai) =>
        [.. items
            .Where(i => (i.Permission is null || granted.Contains(i.Permission)) && (i.AiFeature is null || ai.IsEnabled(i.AiFeature)))
            .OrderBy(i => i.Order)
            .Select(i => new MenuItemResponse(i.Key, i.LabelKey, i.Path, i.Icon, i.Children is null ? null : Filter(i.Children, granted, ai), i.AiFeature is not null))
            .Where(i => i.Path is not null || i.Children is { Count: > 0 })];
}

public sealed record MeResponse(
    UserResponse User,
    TenantResponse Tenant,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<CompanyResponse> Companies,
    IReadOnlyList<MenuItemResponse> Menu,
    PreferencesResponse Preferences,
    IReadOnlyList<CultureResponse> Cultures,
    MeAiResponse Ai);

public sealed record UserResponse(string Subject, string? UserName, string? DisplayName, string? Email);

public sealed record TenantResponse(string Code, string Name);

public sealed record CompanyResponse(Guid Id, string Code, string Name);

/// <summary>Menu entry; <c>Ai</c> marks items that open an AI feature (the web shows the AI badge).</summary>
public sealed record MenuItemResponse(string Key, string LabelKey, string? Path, string? Icon, IReadOnlyList<MenuItemResponse>? Children, bool Ai);

public sealed record PreferencesResponse(string Culture);

public sealed record CultureResponse(string Code, string Name);

/// <param name="Enabled">At least one AI feature is enabled for the tenant (D24).</param>
/// <param name="Features">Enabled AI feature keys; the web shows AI actions only for these.</param>
public sealed record MeAiResponse(bool Enabled, IReadOnlyList<string> Features);
