using System.Security.Claims;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.PostgreSql;
using Merp.Infrastructure.Jobs;
using Merp.Infrastructure.MultiTenancy;
using Merp.Web.MultiTenancy;
using Merp.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Web.Jobs;

public static class JobsHostingExtensions
{
    public const string DashboardPath = "/admin/jobs";
    public const string DashboardCookieScheme = "JobsDashboard";

    /// <summary>
    /// Hangfire with PostgreSQL storage in the catalog database (D3). Jobs restore the tenant and user that enqueued
    /// them. The server can be turned off with <c>Jobs:EnableServer = false</c>.
    /// </summary>
    public static IServiceCollection AddMerpJobs(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddSingleton<JobActivator, TenantAwareJobActivator>();
        services.AddHangfire((serviceProvider, config) => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(
                options => options.UseNpgsqlConnection(configuration.GetConnectionString(MultiTenancyServiceCollectionExtensions.CatalogConnectionStringName)),
                new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true })
            .UseFilter(new TenantJobClientFilter())
            .UseActivator(serviceProvider.GetRequiredService<JobActivator>()));

        if (configuration.GetValue("Jobs:EnableServer", true))
        {
            services.AddHangfireServer(options => options.ServerName = $"merp-{Environment.MachineName}");
            services.AddHostedService<RecurringJobRegistrar>();
        }

        // A short-lived cookie lets the browser open the dashboard, which cannot send bearer tokens.
        services.AddAuthentication().AddCookie(DashboardCookieScheme, options =>
        {
            options.Cookie.Name = "merp.jobs";
            options.Cookie.Path = DashboardPath;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
        });

        return services;
    }

    /// <summary>
    /// Maps <c>POST /api/platform/jobs/dashboard-session</c> (issues the dashboard cookie, needs
    /// <paramref name="permission"/>) and the read-only dashboard at <c>/admin/jobs</c>. The dashboard shows the jobs
    /// of every tenant, so only tenants listed in <c>Jobs:DashboardTenants</c> can open it.
    /// </summary>
    public static IEndpointRouteBuilder MapMerpJobsDashboard(this IEndpointRouteBuilder endpoints, string permission, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var allowedTenants = configuration.GetSection("Jobs:DashboardTenants").Get<string[]>() ?? [];

        endpoints.MapPost("/api/platform/jobs/dashboard-session", async (HttpContext context) =>
            {
                var tenant = context.User.FindFirstValue(TenantResolutionMiddleware.TenantClaim);
                if (tenant is null || !allowedTenants.Contains(tenant, StringComparer.Ordinal))
                {
                    return Results.Forbid();
                }

                var identity = new ClaimsIdentity(
                    [new Claim("sub", context.User.FindFirstValue("sub") ?? string.Empty), new Claim(TenantResolutionMiddleware.TenantClaim, tenant)],
                    DashboardCookieScheme);
                await context.SignInAsync(DashboardCookieScheme, new ClaimsPrincipal(identity));
                return Results.NoContent();
            })
            .RequirePermission(permission)
            .WithTags("platform")
            .WithName("CreateJobsDashboardSession")
            .WithSummary("Issues the short-lived cookie that opens the jobs dashboard");

        endpoints.MapHangfireDashboard(
                DashboardPath,
                new DashboardOptions
                {
                    DashboardTitle = "mERP Jobs",
                    IsReadOnlyFunc = _ => true,
                    AsyncAuthorization = [new CookieDashboardAuthorization(allowedTenants)],
                })
            .AllowAnonymous();

        return endpoints;
    }

    private sealed class CookieDashboardAuthorization(string[] allowedTenants) : IDashboardAsyncAuthorizationFilter
    {
        public async Task<bool> AuthorizeAsync(DashboardContext context)
        {
            var result = await context.GetHttpContext().AuthenticateAsync(DashboardCookieScheme);
            var tenant = result.Principal?.FindFirstValue(TenantResolutionMiddleware.TenantClaim);
            return result.Succeeded && tenant is not null && allowedTenants.Contains(tenant, StringComparer.Ordinal);
        }
    }
}
