using System.Globalization;
using System.Security.Claims;
using Merp.Application.Abstractions.AI;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Localization;
using Merp.Modules.Platform.Application.Companies;
using Merp.Modules.Platform.Application.Users;
using Merp.Web.Errors;
using Merp.Web.Navigation;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Platform.Api.Endpoints;

internal static class MeEndpoints
{
    /// <summary>
    /// The signed-in user, tenant, granted permissions and menu, used by the web shell. Every signed-in user may
    /// read their own data (D22). Permissions come from the role assignments in force (P2); companies are those the
    /// granted permissions reach (P1).
    /// </summary>
    public static void MapMeEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/me", GetMeAsync)
            .RequireAuthenticatedUser()
            .WithName("GetMe")
            .WithSummary("Current user, tenant, permissions and menu");

        group.MapPut("/me/preferences", UpdatePreferencesAsync)
            .RequireAuthenticatedUser()
            .WithName("UpdateMyPreferences")
            .WithSummary("The signed-in user's preferences (culture); a null culture follows the browser")
            .ProducesValidationProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UpdatePreferencesAsync(
        UpdateMyPreferencesCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
    }

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
        ICurrentUserProvisioner provisioner,
        ISupportedCultures supportedCultures)
    {
        var granted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var permission in permissionProviders.SelectMany(p => p.GetPermissions()).Select(p => p.Code).Distinct(StringComparer.Ordinal))
        {
            if (await permissionChecker.HasPermissionAsync(permission, context.RequestAborted))
            {
                granted.Add(permission);
            }
        }

        var companies = await sender.Send(new GetSelectableCompaniesQuery(granted), context.RequestAborted);
        var menu = Filter(menuContributors.SelectMany(c => c.GetMenuItems()), granted, ai);
        var user = context.User;
        var cultures = supportedCultures.All
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

        var preferred = context.User.FindFirstValue("sub") is { } subject
            ? (await provisioner.ProvisionAsync(new IdentityClaims(
                subject,
                context.User.FindFirstValue("preferred_username"),
                context.User.FindFirstValue("email"),
                context.User.FindFirstValue("given_name"),
                context.User.FindFirstValue("family_name"),
                context.User.FindFirstValue("name")), context.RequestAborted)).Culture
            : null;

        return new MeResponse(
            new UserResponse(
                currentUser.UserId,
                user.FindFirstValue("sub") ?? string.Empty,
                user.FindFirstValue("preferred_username"),
                user.FindFirstValue("name"),
                user.FindFirstValue("email")),
            new TenantResponse(tenant.RequiredTenant.Code, tenant.RequiredTenant.Name),
            [.. granted.Order(StringComparer.Ordinal)],
            [.. companies.Value.Select(c => new CompanyResponse(c.Id, c.Code, c.Name))],
            menu,
            new PreferencesResponse(currentUser.Culture, preferred),
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

/// <param name="Id">Platform user id.</param>
/// <param name="Subject">Keycloak subject.</param>
/// <param name="UserName">User name.</param>
/// <param name="DisplayName">Name to show.</param>
/// <param name="Email">E-mail address.</param>
public sealed record UserResponse(Guid? Id, string Subject, string? UserName, string? DisplayName, string? Email);

public sealed record TenantResponse(string Code, string Name);

public sealed record CompanyResponse(Guid Id, string Code, string Name);

/// <summary>Menu entry; <c>Ai</c> marks items that open an AI feature (the web shows the AI badge).</summary>
public sealed record MenuItemResponse(string Key, string LabelKey, string? Path, string? Icon, IReadOnlyList<MenuItemResponse>? Children, bool Ai);

/// <param name="Culture">Culture in effect for this request.</param>
/// <param name="PreferredCulture">Culture the user chose; null follows the browser.</param>
public sealed record PreferencesResponse(string Culture, string? PreferredCulture);

public sealed record CultureResponse(string Code, string Name);

/// <param name="Enabled">At least one AI feature is enabled for the tenant (D24).</param>
/// <param name="Features">Enabled AI feature keys; the web shows AI actions only for these.</param>
public sealed record MeAiResponse(bool Enabled, IReadOnlyList<string> Features);
