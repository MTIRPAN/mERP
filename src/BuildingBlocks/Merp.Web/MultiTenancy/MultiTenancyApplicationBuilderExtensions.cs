using Microsoft.AspNetCore.Builder;

namespace Merp.Web.MultiTenancy;

public static class MultiTenancyApplicationBuilderExtensions
{
    /// <summary>Must run after authentication and before authorization.</summary>
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app) =>
        app.UseMiddleware<AmbientServiceScopeMiddleware>().UseMiddleware<TenantResolutionMiddleware>();
}
