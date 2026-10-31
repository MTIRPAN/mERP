namespace Merp.Modules.Recruitment.Application;

/// <summary>AI features of the Recruitment module; tenants enable them one by one (D24).</summary>
public static class RecruitmentAiFeatures
{
    /// <summary>Structured, anonymized CV analysis and job match (docs/domain/yapay-zeka.md §3.1).</summary>
    public const string CvAnalysis = "recruitment.cv_analysis";

    /// <summary>Drafts of opening texts and the AI review of their language (docs/domain/yapay-zeka.md §3.4, D57).</summary>
    public const string OpeningText = "recruitment.opening_text";
}
