using Merp.Infrastructure.Messaging;
using Merp.Infrastructure.Persistence;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Platform.Infrastructure;

public static class PlatformInfrastructureServiceCollectionExtensions
{
    /// <summary>Platform persistence and handlers. Used by the API host and the migrator.</summary>
    public static IServiceCollection AddPlatformInfrastructure(this IServiceCollection services)
    {
        services.AddModuleDbContext<PlatformDbContext>(Application.AssemblyReference.Assembly);
        services.AddRequestHandlersFromAssembly(Application.AssemblyReference.Assembly);
        return services;
    }
}
