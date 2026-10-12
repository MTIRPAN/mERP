using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Infrastructure;
using Merp.Web.Modules;
using Merp.Web.Navigation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Platform.Api;

public sealed class PlatformModule : IModule
{
    public string Name => "platform";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddPlatformInfrastructure(configuration);
        services.AddSingleton<IMenuContributor, PlatformMenu>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapModuleGroup(this);
        group.MapSystemEndpoints();
        group.MapLocalizationEndpoints();
        group.MapMeEndpoints();
        group.MapAiUsageEndpoints();
        group.MapCompanyEndpoints();
        group.MapBranchEndpoints();
        group.MapLocationEndpoints();
        group.MapUserEndpoints();
        group.MapRoleEndpoints();
        group.MapAuditEndpoints();
        group.MapNumberingEndpoints();
        group.MapCustomFieldEndpoints();
        group.MapFileEndpoints();
        group.MapNotificationEndpoints();
        group.MapCalendarEndpoints();
        group.MapApprovalEndpoints();
    }
}
