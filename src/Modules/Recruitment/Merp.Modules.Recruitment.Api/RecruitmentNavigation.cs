using Merp.Application.Abstractions.AI;
using Merp.Modules.Recruitment.Application;
using Merp.Web.Navigation;

namespace Merp.Modules.Recruitment.Api;

internal sealed class RecruitmentMenu : IMenuContributor
{
    public IEnumerable<MenuItem> GetMenuItems() =>
    [
        new("recruitment", "web.shell.menu.recruitment", Icon: "recruitment", Order: 300, Children:
        [
            new(
                "cv-analysis",
                "web.shell.menu.cv_analysis",
                Path: "/recruitment/cv-analysis",
                Icon: "ai",
                Permission: RecruitmentPermissions.Cv.Analyze,
                AiFeature: RecruitmentAiFeatures.CvAnalysis),
            new(
                "requisitions",
                "web.shell.menu.requisitions",
                Path: "/recruitment/requisitions",
                Icon: "requisition",
                Permission: RecruitmentPermissions.Requisitions.Read,
                Order: 10),
            new(
                "openings",
                "web.shell.menu.openings",
                Path: "/recruitment/openings",
                Icon: "opening",
                Permission: RecruitmentPermissions.Openings.Read,
                Order: 20),
            new(
                "candidates",
                "web.shell.menu.candidates",
                Path: "/recruitment/candidates",
                Icon: "candidates",
                Permission: RecruitmentPermissions.Candidates.Read,
                Order: 30),
            new(
                "offers",
                "web.shell.menu.offers",
                Path: "/recruitment/offers",
                Icon: "offers",
                Permission: RecruitmentPermissions.Offers.Read,
                Order: 35),
            new(
                "recruitment-reports",
                "web.shell.menu.recruitment_reports",
                Path: "/recruitment/reports",
                Icon: "recruitment-reports",
                Permission: RecruitmentPermissions.Reports.Read,
                Order: 38),
            new(
                "my-interviews",
                "web.shell.menu.my_interviews",
                Path: "/recruitment/my-interviews",
                Icon: "interviews",
                Permission: RecruitmentPermissions.Interviews.Read,
                Order: 40),
            new(
                "my-openings",
                "web.shell.menu.my_openings",
                Path: "/recruitment/my-openings",
                Icon: "my-openings",
                Permission: RecruitmentPermissions.Self.Apply,
                Order: 50),
            new(
                "recruitment-settings",
                "web.shell.menu.recruitment_settings",
                Path: "/recruitment/settings",
                Icon: "settings",
                Permission: RecruitmentPermissions.Settings.Read,
                Order: 100),
        ]),
    ];
}

internal sealed class RecruitmentAiFeatureDefinitions : IAiFeatureDefinitionProvider
{
    public IEnumerable<AiFeatureDefinition> GetFeatures() =>
    [
        new(RecruitmentAiFeatures.CvAnalysis),
        new(RecruitmentAiFeatures.OpeningText),
    ];
}
