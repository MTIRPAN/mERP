using Merp.Modules.Platform.Application.Common;
using Merp.Modules.Platform.Application.Locations;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Platform.Application;

public static class PlatformApplicationServiceCollectionExtensions
{
    /// <summary>Services the Platform handlers share.</summary>
    public static IServiceCollection AddPlatformApplication(this IServiceCollection services)
    {
        services.AddScoped<PlatformAccess>();
        services.AddScoped<LocationResolver>();
        return services;
    }
}
