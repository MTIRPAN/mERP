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
        ]),
    ];
}

internal sealed class RecruitmentAiFeatureDefinitions : IAiFeatureDefinitionProvider
{
    public IEnumerable<AiFeatureDefinition> GetFeatures() =>
    [
        new(RecruitmentAiFeatures.CvAnalysis),
    ];
}
