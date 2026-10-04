using Microsoft.AspNetCore.Builder;
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
        return builder;
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
