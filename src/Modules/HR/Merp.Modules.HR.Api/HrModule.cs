using Merp.Modules.HR.Api.Endpoints;
using Merp.Modules.HR.Infrastructure;
using Merp.Web.Modules;
using Merp.Web.Navigation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.HR.Api;

/// <summary>
/// Human resources (PLAN Faz 2). H1 brings the organization: units, the job catalog, positions and reporting lines
/// (D37); employees, leave and the rest follow with H2–L6.
/// </summary>
public sealed class HrModule : IModule
{
    public string Name => "hr";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHrInfrastructure();
        services.AddSingleton<IMenuContributor, HrMenu>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapModuleGroup(this);
        group.MapOrgUnitEndpoints();
        group.MapJobEndpoints();
        group.MapPositionEndpoints();
        group.MapEmployeeEndpoints();
        group.MapSgkExitCodeEndpoints();
        group.MapDocumentEndpoints();
        group.MapSelfServiceEndpoints();
        group.MapLeaveSettingsEndpoints();
        group.MapLeaveBalanceEndpoints();
        group.MapLeaveRequestEndpoints();
        group.MapLeaveReportEndpoints();
    }
}
