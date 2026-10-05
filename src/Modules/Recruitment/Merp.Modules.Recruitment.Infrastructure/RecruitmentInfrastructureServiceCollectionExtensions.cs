using Merp.Infrastructure.Messaging;
using Merp.Infrastructure.Persistence;
using Merp.Modules.Recruitment.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Recruitment.Infrastructure;

public static class RecruitmentInfrastructureServiceCollectionExtensions
{
    /// <summary>Recruitment persistence and handlers. Used by the API host and the migrator.</summary>
    public static IServiceCollection AddRecruitmentInfrastructure(this IServiceCollection services)
    {
        services.AddModuleDbContext<RecruitmentDbContext>(Application.AssemblyReference.Assembly);
        services.AddRequestHandlersFromAssembly(Application.AssemblyReference.Assembly);
        return services;
    }
}
