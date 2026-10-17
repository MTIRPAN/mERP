using Merp.Modules.HR.Application;
using Merp.Web.Navigation;

namespace Merp.Modules.HR.Api;

/// <summary>Menu of HR (PLAN H1b): the organization, its positions and the job catalog.</summary>
internal sealed class HrMenu : IMenuContributor
{
    public IEnumerable<MenuItem> GetMenuItems() =>
    [
        new("hr", "web.shell.menu.hr", Icon: "hr", Order: 200, Children:
        [
            new("hr-organization", "web.shell.menu.hr_organization", Path: "/hr/organization", Icon: "organization", Permission: HrPermissions.Org.Read),
            new("hr-positions", "web.shell.menu.hr_positions", Path: "/hr/positions", Icon: "positions", Permission: HrPermissions.Org.Read, Order: 10),
            new("hr-job-titles", "web.shell.menu.hr_job_titles", Path: "/hr/job-titles", Icon: "job-titles", Permission: HrPermissions.Org.Read, Order: 20),
        ]),
    ];
}
