using System.Globalization;
using System.Security.Claims;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Security;
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
/// <item>the culture: the user's preference, otherwise the one chosen by request localization (D11);</item>
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
        IProblemDetailsService problemDetails)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(companyContext);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(problemDetails);

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

            var culture = user.Culture is { } preferred && context.RequestServices.GetRequiredService<ISupportedCultures>().IsSupported(preferred)
                ? preferred
                : null;
            if (culture is not null)
            {
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
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
