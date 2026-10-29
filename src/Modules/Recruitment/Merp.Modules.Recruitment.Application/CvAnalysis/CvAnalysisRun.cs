using Merp.Application.Abstractions.AI;
using Merp.Application.Abstractions.Documents;
using Merp.SharedKernel.Privacy;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Recruitment.Application.CvAnalysis;

/// <summary>
/// One CV analysis (D24): the text read from the file, redacted (names, contact details, identifiers, sensitive fields),
/// sent with the fixed instructions, and the result normalized. Used by the stateless analysis (A2) and by the
/// analysis of applications and the candidate form (A3).
/// </summary>
internal static class CvAnalysisRun
{
    public static async Task<Result<CvAnalysisResponse>> AnalyzeAsync(
        IAiService ai,
        ExtractedDocument document,
        IEnumerable<string> names,
        string? jobTitle,
        string? jobRequirements,
        string culture,
        CancellationToken cancellationToken)
    {
        // Data minimization (KVKK md. 4): contact details, identifiers, the name and sensitive fields never leave.
        var redacted = PersonalDataRedactor.Redact(document.Text, names);
        var response = await ai.GenerateAsync<CvAnalysisResult>(
            new AiRequest
            {
                Feature = RecruitmentAiFeatures.CvAnalysis,
                PromptVersion = CvAnalysisPrompt.Version,
                Instructions = CvAnalysisPrompt.Instructions,
                Input = CvAnalysisPrompt.BuildInput(redacted.Text, jobTitle, jobRequirements, culture),
                Effort = AiEffort.Medium,
            },
            cancellationToken);
        if (response.IsFailure)
        {
            return response.Error;
        }

        return new CvAnalysisResponse(
            Normalize(response.Value.Value, string.IsNullOrWhiteSpace(jobRequirements)),
            [.. redacted.Counts.OrderBy(c => c.Key).Select(c => new RedactionCount(KindCode(c.Key), c.Value))],
            document.Truncated,
            response.Value.Usage.Model,
            CvAnalysisPrompt.Version);
    }

    // The schema cannot express ranges, so they are enforced here.
    private static CvAnalysisResult Normalize(CvAnalysisResult analysis, bool withoutRequirements) => analysis with
    {
        TotalExperienceYears = analysis.TotalExperienceYears is { } years ? Math.Clamp(Math.Round(years, 1), 0m, 60m) : null,
        Match = withoutRequirements || analysis.Match is null ? null : analysis.Match with { Score = Math.Clamp(analysis.Match.Score, 0, 100) },
    };

    private static string KindCode(RedactionKind kind) => kind switch
    {
        RedactionKind.Email => "email",
        RedactionKind.Phone => "phone",
        RedactionKind.NationalId => "national_id",
        RedactionKind.Iban => "iban",
        RedactionKind.Url => "url",
        RedactionKind.Name => "name",
        _ => "sensitive_field",
    };
}
