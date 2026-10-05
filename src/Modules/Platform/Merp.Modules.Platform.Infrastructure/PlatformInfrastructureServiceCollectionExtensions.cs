using Merp.Infrastructure.AI;
using Merp.Infrastructure.Messaging;
using Merp.Infrastructure.Persistence;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Organization;
using Merp.Modules.Platform.Infrastructure.AI;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Merp.Modules.Platform.Infrastructure;

public static class PlatformInfrastructureServiceCollectionExtensions
{
    /// <summary>Platform persistence and handlers. Used by the API host and the migrator.</summary>
    public static IServiceCollection AddPlatformInfrastructure(this IServiceCollection services)
    {
        services.AddModuleDbContext<PlatformDbContext>(Application.AssemblyReference.Assembly);
        services.AddScoped<IPlatformDbContext>(provider => provider.GetRequiredService<PlatformDbContext>());
        services.AddRequestHandlersFromAssembly(Application.AssemblyReference.Assembly);
        services.AddScoped<AddressResolver>();

        // Platform owns the AI usage ledger of all modules (D24).
        services.Replace(ServiceDescriptor.Scoped<IAiUsageLedger, PlatformAiUsageLedger>());
        services.AddScoped<AiUsageReader>();
        return services;
    }
}
