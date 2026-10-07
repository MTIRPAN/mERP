using System.Globalization;
using System.Security.Claims;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Security;
using Merp.Infrastructure.Localization;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.Security;
using Merp.Web.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Web.Hosting;

/// <summary>
/// Binds the per-request context:
/// <list type="bullet">
/// <item>the platform user of the signed-in identity (created on first sign-in, D6); inactive users get 403;</item>
/// <item>the tenant's cultures and translations, and the culture: the user's preference, otherwise the best match of
/// <c>Accept-Language</c> among the tenant's active cultures, otherwise the tenant's default (D11, D28);</item>
/// <item>the active company from the <c>X-Company-Id</c> header (D4), which narrows data and never widens permissions.</item>
/// </list>
/// </summary>
public sealed class RequestContextMiddleware(RequestDelegate next)
{
    public const string CompanyHeader = "X-Company-Id";
    public const string UserInactiveCode = "common.errors.user_inactive";

    public async Task InvokeAsync(
        HttpContext context,
        CurrentUser currentUser,
        CompanyContext companyContext,
        TenantContext tenantContext,
        LocalizationContext localization,
        IProblemDetailsService problemDetails)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(companyContext);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(problemDetails);

        if (tenantContext.Tenant is not null)
        {
            var snapshot = await context.RequestServices.GetRequiredService<ILocalizationSource>().GetAsync(context.RequestAborted);
            localization.Set(snapshot);
            ApplyCulture(context, RequestedCulture(context, snapshot) ?? snapshot.DefaultCulture);
        }

        currentUser.SetCulture(CultureInfo.CurrentUICulture.Name);

        if (context.User.Identity?.IsAuthenticated == true
            && tenantContext.Tenant is { } tenant
            && context.User.FindFirstValue("sub") is { Length: > 0 } subject
            && context.RequestServices.GetService<ICurrentUserProvisioner>() is { } provisioner)
        {
            var user = await provisioner.ProvisionAsync(Identity(context.User, subject), context.RequestAborted);
            if (!user.IsActive)
            {
                await WriteProblemAsync(context, problemDetails, StatusCodes.Status403Forbidden, UserInactiveCode);
                return;
            }

            // A preference for a culture the tenant has switched off is ignored
            var culture = localization.Snapshot.Find(user.Culture)?.Code;
            if (culture is not null)
            {
                ApplyCulture(context, culture);
            }

            currentUser.SetUser(user.UserId, tenant.Id, user.EmployeeId, culture);
        }

        if (context.Request.Headers.TryGetValue(CompanyHeader, out var header) && !string.IsNullOrWhiteSpace(header))
        {
            if (!Guid.TryParse(header, out var companyId) || companyId == Guid.Empty)
            {
                await WriteProblemAsync(context, problemDetails, StatusCodes.Status400BadRequest, "common.errors.invalid_company_header");
                return;
            }

            companyContext.Set(companyId);
        }

        await next(context);
    }

    /// <summary>The best active culture of <c>Accept-Language</c>, by quality.</summary>
    private static string? RequestedCulture(HttpContext context, LocalizationSnapshot snapshot) =>
        context.Request.GetTypedHeaders().AcceptLanguage
            .Where(l => l.Quality is not 0)
            .OrderByDescending(l => l.Quality ?? 1)
            .Select(l => l.Value.Value is { } value ? snapshot.Match(value) : null)
            .FirstOrDefault(c => c is not null);

    private static void ApplyCulture(HttpContext context, string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        context.Response.Headers.ContentLanguage = culture;
    }

    private static IdentityClaims Identity(ClaimsPrincipal user, string subject) =>
        new(
            subject,
            user.FindFirstValue("preferred_username"),
            user.FindFirstValue("email"),
            user.FindFirstValue("given_name"),
            user.FindFirstValue("family_name"),
            user.FindFirstValue("name"));

    private static async Task WriteProblemAsync(HttpContext context, IProblemDetailsService problemDetails, int status, string code)
    {
        context.Response.StatusCode = status;
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = status, Extensions = { [LocalizedProblemDetails.CodeKey] = code } },
        });
    }
}
