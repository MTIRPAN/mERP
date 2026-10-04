using Merp.Modules.Platform.Infrastructure;
using Merp.Web.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Platform.Api;

public sealed class PlatformModule : IModule
{
    public string Name => "platform";

    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddPlatformInfrastructure();

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Platform endpoints are added by the Phase 1 tasks (P1...P13).
        endpoints.MapGroup("/api/platform");
    }
}
