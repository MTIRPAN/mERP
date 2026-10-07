using Merp.Infrastructure.Localization;
using Merp.Infrastructure.Messaging;
using Merp.Application.Abstractions.Numbering;
using Merp.Application.Abstractions.Security;
using Merp.Infrastructure.Persistence;
using Merp.Modules.Recruitment.Application;
using Merp.Modules.Recruitment.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Recruitment.Infrastructure;

public static class RecruitmentInfrastructureServiceCollectionExtensions
{
    /// <summary>Recruitment persistence, handlers, permission catalog and numbered documents. Used by the API host and the migrator.</summary>
    public static IServiceCollection AddRecruitmentInfrastructure(this IServiceCollection services)
    {
        services.AddModuleDbContext<RecruitmentDbContext>(Application.AssemblyReference.Assembly);
        services.AddRequestHandlersFromAssembly(Application.AssemblyReference.Assembly);
        services.AddSingleton<IPermissionDefinitionProvider, RecruitmentPermissionDefinitions>();
        services.AddSingleton<INumberSequenceDefinitionProvider, RecruitmentNumberSequences>();
        services.AddLocalizationResources(Application.AssemblyReference.Assembly);
        return services;
    }
}
