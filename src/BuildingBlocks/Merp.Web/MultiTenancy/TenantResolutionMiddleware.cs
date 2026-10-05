using System.Security.Claims;
using Merp.Infrastructure.MultiTenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Merp.Web.MultiTenancy;

/// <summary>
/// Resolves the tenant of the request (D3):
/// <list type="bullet">
/// <item>authenticated requests use the <c>tenant</c> claim of the token; unknown or inactive tenant → 401;</item>
/// <item>anonymous public endpoints (<c>/api/public/{tenantCode}/...</c>) use the route; unknown or inactive → 404.</item>
/// </list>
/// Nothing else (headers, query strings) can select a tenant.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public const string TenantClaim = "tenant";
    public const string PublicTenantRouteValue = "tenantCode";
    public const string PublicPathPrefix = "/api/public/";
    public const string UnknownTenantCode = "common.errors.unknown_tenant";

    public async Task InvokeAsync(HttpContext context, ITenantStore tenantStore, TenantContext tenantContext, IProblemDetailsService problemDetails)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantStore);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(problemDetails);

        if (IsPublicTenantRoute(context, out var publicTenantCode))
        {
            // Public endpoints are anonymous: a token issued for another tenant must not leak into them.
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
            var tenant = await tenantStore.FindByCodeAsync(publicTenantCode, context.RequestAborted);
            if (tenant is not { IsActive: true })
            {
                await WriteProblemAsync(context, problemDetails, StatusCodes.Status404NotFound, "common.errors.not_found");
                return;
            }

            tenantContext.Set(tenant.ToTenantInfo(), tenant.ConnectionString);
        }
        else if (context.User.Identity?.IsAuthenticated == true)
        {
            var code = context.User.FindFirstValue(TenantClaim);
            var tenant = string.IsNullOrWhiteSpace(code) ? null : await tenantStore.FindByCodeAsync(code, context.RequestAborted);
            if (tenant is not { IsActive: true })
            {
                await WriteProblemAsync(context, problemDetails, StatusCodes.Status401Unauthorized, UnknownTenantCode);
                return;
            }

            tenantContext.Set(tenant.ToTenantInfo(), tenant.ConnectionString);
        }

        await next(context);
    }

    private static bool IsPublicTenantRoute(HttpContext context, out string tenantCode)
    {
        tenantCode = string.Empty;
        if (!context.Request.Path.StartsWithSegments(PublicPathPrefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            || context.GetRouteValue(PublicTenantRouteValue) is not string code)
        {
            return false;
        }

        tenantCode = code;
        return true;
    }

    private static async Task WriteProblemAsync(HttpContext context, IProblemDetailsService problemDetails, int status, string code)
    {
        context.Response.StatusCode = status;
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = status, Extensions = { ["code"] = code } },
        });
    }
}
