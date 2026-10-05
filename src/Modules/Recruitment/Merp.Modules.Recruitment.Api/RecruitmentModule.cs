using Merp.Application.Abstractions.AI;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Recruitment.Api.Endpoints;
using Merp.Modules.Recruitment.Infrastructure;
using Merp.Web.Modules;
using Merp.Web.Navigation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Recruitment.Api;

/// <summary>
/// Recruitment (PLAN Faz 4). For now only the AI CV analysis exists (D24); requisitions, openings, candidates and
/// the rest follow with R1–R9.
/// </summary>
public sealed class RecruitmentModule : IModule
{
    public string Name => "recruitment";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddRecruitmentInfrastructure();
        services.AddSingleton<IMenuContributor, RecruitmentMenu>();
        services.AddSingleton<IPermissionDefinitionProvider, RecruitmentPermissionDefinitions>();
        services.AddSingleton<IAiFeatureDefinitionProvider, RecruitmentAiFeatureDefinitions>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapModuleGroup(this);
        group.MapCvAnalysisEndpoints();
    }
}
