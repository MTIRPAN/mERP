using Merp.Application.Abstractions.AI;
using Merp.Modules.Recruitment.Api.Endpoints;
using Merp.Modules.Recruitment.Infrastructure;
using Merp.Web.Modules;
using Merp.Web.Navigation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Recruitment.Api;

/// <summary>
/// Recruitment (PLAN Faz 4): the AI CV analysis (D24) and the settings (R1, D47); requisitions, openings,
/// candidates and the rest follow with R2–R9.
/// </summary>
public sealed class RecruitmentModule : IModule
{
    public string Name => "recruitment";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddRecruitmentInfrastructure();
        services.AddSingleton<IMenuContributor, RecruitmentMenu>();
        services.AddSingleton<IAiFeatureDefinitionProvider, RecruitmentAiFeatureDefinitions>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapModuleGroup(this);
        group.MapCvAnalysisEndpoints();
        group.MapSettingsEndpoints();
    }
}
