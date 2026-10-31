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
/// Recruitment (PLAN Faz 4): the AI CV analysis (D24), the settings (R1, D47), the requisitions (R2, D48), the
/// openings, the career page and applications (R3, D49), the pipeline and the candidate pool (R4, D50), interviews and evaluations (R5, D52); the rest follows with R6–R9.
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
        group.MapRequisitionEndpoints();
        group.MapOpeningEndpoints();
        group.MapApplicationEndpoints();
        group.MapCandidateEndpoints();
        group.MapPrivacyEndpoints();
        group.MapInterviewEndpoints();
        group.MapOfferEndpoints();
        group.MapReportEndpoints();
        group.MapSelfServiceCareerEndpoints();
        group.MapHiringTeamEndpoints();
        group.MapHiringTeamCandidateEndpoints();
        group.MapSettingsEndpoints();
        endpoints.MapPublicCareerEndpoints();
        endpoints.MapPublicOfferEndpoints();
        endpoints.MapPublicConsentEndpoints();
    }
}
