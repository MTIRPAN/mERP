using Merp.Infrastructure.Localization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Web.Modules;

public static class ModuleExtensions
{
    /// <summary>Registers a module's services and remembers the module for <see cref="MapModules"/>.</summary>
    public static WebApplicationBuilder AddModule<TModule>(this WebApplicationBuilder builder)
        where TModule : IModule, new()
    {
        ArgumentNullException.ThrowIfNull(builder);
        var module = new TModule();
        module.AddServices(builder.Services, builder.Configuration);
        builder.Services.AddSingleton<IModule>(module);
        // Default texts of the module (Localization/tr-TR.json embedded in the module's Api assembly)
        builder.Services.AddLocalizationResources(typeof(TModule).Assembly);
        return builder;
    }

    /// <summary>Endpoint group <c>/api/{module}</c> tagged with the module name (CLAUDE.md: endpoint paths).</summary>
    public static RouteGroupBuilder MapModuleGroup(this IEndpointRouteBuilder endpoints, IModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        return endpoints.MapGroup($"/api/{module.Name}").WithTags(module.Name);
    }

    /// <summary>Maps the endpoints of every registered module.</summary>
    public static IEndpointRouteBuilder MapModules(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        foreach (var module in endpoints.ServiceProvider.GetServices<IModule>())
        {
            module.MapEndpoints(endpoints);
        }

        return endpoints;
    }
}
