using Merp.Modules.HR.Application;
using Merp.Web.Navigation;

namespace Merp.Modules.HR.Api;

/// <summary>
/// Menu of HR (PLAN H1b, H2c, H3c, L1, L4): the person's own card and leave, employees, leave requests, the
/// organization, its positions, the job catalog, the leave settings and document types.
/// </summary>
internal sealed class HrMenu : IMenuContributor
{
    public IEnumerable<MenuItem> GetMenuItems() =>
    [
        new("hr", "web.shell.menu.hr", Icon: "hr", Order: 200, Children:
        [
            new("hr-me", "web.shell.menu.hr_me", Path: "/hr/me", Icon: "my-card", Permission: HrPermissions.Self.ProfileRead, Order: -20),
            new("hr-my-leave", "web.shell.menu.hr_my_leave", Path: "/hr/me/leave", Icon: "my-leave", Permission: HrPermissions.Self.LeaveRead, Order: -15),
            new("hr-employees", "web.shell.menu.hr_employees", Path: "/hr/employees", Icon: "employees", Permission: HrPermissions.Employees.Read, Order: -10),
            new("hr-pre-hires", "web.shell.menu.hr_pre_hires", Path: "/hr/pre-hires", Icon: "pre-hires", Permission: HrPermissions.Employees.Create, Order: -9),
            new("hr-leave-requests", "web.shell.menu.hr_leave_requests", Path: "/hr/leave-requests", Icon: "leave-requests", Permission: HrPermissions.Leave.Requests.Read, Order: -5),
            new("hr-leave-calendar", "web.shell.menu.hr_leave_calendar", Path: "/hr/leave-calendar", Icon: "leave-calendar", Permission: HrPermissions.Leave.Calendar.Read, Order: -4),
            new("hr-leave-reports", "web.shell.menu.hr_leave_reports", Path: "/hr/leave-reports", Icon: "leave-reports", Permission: HrPermissions.Leave.Reports.Read, Order: -3),
            new("hr-organization", "web.shell.menu.hr_organization", Path: "/hr/organization", Icon: "organization", Permission: HrPermissions.Org.Read),
            new("hr-positions", "web.shell.menu.hr_positions", Path: "/hr/positions", Icon: "positions", Permission: HrPermissions.Org.Read, Order: 10),
            new("hr-job-titles", "web.shell.menu.hr_job_titles", Path: "/hr/job-titles", Icon: "job-titles", Permission: HrPermissions.Org.Read, Order: 20),
            new("hr-leave-settings", "web.shell.menu.hr_leave_settings", Path: "/hr/leave-settings", Icon: "leave-settings", Permission: HrPermissions.Leave.Settings.Read, Order: 25),
            new("hr-document-types", "web.shell.menu.hr_document_types", Path: "/hr/document-types", Icon: "document-types", Permission: HrPermissions.Employees.Documents.Manage, Order: 30),
        ]),
    ];
}
