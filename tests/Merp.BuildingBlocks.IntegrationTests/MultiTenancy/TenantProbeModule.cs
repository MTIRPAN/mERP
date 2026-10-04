using Merp.Application.Abstractions.Context;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Merp.Web.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.BuildingBlocks.IntegrationTests.MultiTenancy;

/// <summary>Test-only endpoints that report which tenant database a request reached.</summary>
public sealed class TenantProbeModule : IModule
{
    public string Name => "probe";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/probe/tenant", ProbeAsync).RequireAuthorization();
        endpoints.MapGet("/api/public/{tenantCode}/probe", ProbeAsync).AllowAnonymous();
    }

    private static async Task<IResult> ProbeAsync(HttpContext context, ITenantContext tenant, PlatformDbContext db) =>
        Results.Ok(new TenantProbe(
            tenant.Tenant?.Code,
            context.User.Identity?.IsAuthenticated == true,
            await db.AuditEntries.Select(e => e.EntityType).ToListAsync(context.RequestAborted)));
}

public sealed record TenantProbe(string? Tenant, bool Authenticated, List<string> Data);
