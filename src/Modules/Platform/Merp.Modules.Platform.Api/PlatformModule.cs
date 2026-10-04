using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Infrastructure;
using Merp.Web.Modules;
using Merp.Web.Navigation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Platform.Api;

public sealed class PlatformModule : IModule
{
    public string Name => "platform";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddPlatformInfrastructure();
        services.AddSingleton<IMenuContributor, PlatformMenu>();
        services.AddSingleton<IPermissionDefinitionProvider, PlatformPermissionDefinitions>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapModuleGroup(this);
        group.MapSystemEndpoints();
        group.MapLocalizationEndpoints();
        group.MapMeEndpoints();
    }
}
