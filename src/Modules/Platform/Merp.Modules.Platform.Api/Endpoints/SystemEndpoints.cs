using System.Reflection;
using System.Security.Claims;
using Merp.Application.Abstractions.Context;
using Merp.Modules.Platform.Application;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Merp.Modules.Platform.Api.Endpoints;

internal static class SystemEndpoints
{
    public static void MapSystemEndpoints(this IEndpointRouteBuilder group) =>
        group.MapGet("/system/info", GetInfo)
            .RequirePermission(PlatformPermissions.SystemInfo.Read)
            .WithName("GetSystemInfo")
            .WithSummary("Version, environment, current tenant and caller");

    private static SystemInfoResponse GetInfo(HttpContext context, ITenantContext tenant, ICurrentUser currentUser, IHostEnvironment environment) =>
        new(
            typeof(SystemEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0",
            environment.EnvironmentName,
            tenant.RequiredTenant.Code,
            tenant.RequiredTenant.Name,
            context.User.FindFirstValue("sub"),
            context.User.Identity?.Name,
            currentUser.Culture);
}

public sealed record SystemInfoResponse(
    string Version,
    string Environment,
    string TenantCode,
    string TenantName,
    string? Subject,
    string? UserName,
    string Culture);
