using Merp.Application.Abstractions.AI;
using Merp.Application.Abstractions.Documents;
using Merp.SharedKernel.Privacy;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Recruitment.Application.CvAnalysis;

/// <summary>A CV ready to send: the redacted input with what the stored analysis keeps about it.</summary>
/// <param name="Input">The model input: output language, job and redacted CV.</param>
/// <param name="Redactions">How many values of each kind were removed.</param>
/// <param name="Truncated">The document was longer than what is read.</param>
/// <param name="WithoutRequirements">No requirements were given, so no match is kept.</param>
public sealed record CvAnalysisInput(string Input, IReadOnlyList<RedactionCount> Redactions, bool Truncated, bool WithoutRequirements);

/// <summary>
/// One CV analysis (D24): the text read from the file, redacted (names, contact details, identifiers, sensitive fields),
/// sent with the fixed instructions, and the result normalized. Used by the stateless analysis (A2), by the
/// analysis of applications and the candidate form (A3), and by the batch screening (A6), which sends the same input.
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
        var input = Prepare(document, names, jobTitle, jobRequirements, culture);
        var response = await ai.GenerateAsync<CvAnalysisResult>(
            new AiRequest
            {
                Feature = RecruitmentAiFeatures.CvAnalysis,
                PromptVersion = CvAnalysisPrompt.Version,
                Instructions = CvAnalysisPrompt.Instructions,
                Input = input.Input,
                Effort = AiEffort.Medium,
            },
            cancellationToken);
        return response.IsFailure ? response.Error : Respond(response.Value.Value, response.Value.Usage.Model, input);
    }

    /// <summary>Redacts the CV and builds the input.</summary>
    public static CvAnalysisInput Prepare(ExtractedDocument document, IEnumerable<string> names, string? jobTitle, string? jobRequirements, string culture)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Data minimization (KVKK md. 4): contact details, identifiers, the name and sensitive fields never leave.
        var redacted = PersonalDataRedactor.Redact(document.Text, names);
        return new CvAnalysisInput(
            CvAnalysisPrompt.BuildInput(redacted.Text, jobTitle, jobRequirements, culture),
            [.. redacted.Counts.OrderBy(c => c.Key).Select(c => new RedactionCount(KindCode(c.Key), c.Value))],
            document.Truncated,
            string.IsNullOrWhiteSpace(jobRequirements));
    }

    /// <summary>The model's analysis of a prepared CV as it is shown and stored.</summary>
    public static CvAnalysisResponse Respond(CvAnalysisResult analysis, string model, CvAnalysisInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new CvAnalysisResponse(Normalize(analysis, input.WithoutRequirements), input.Redactions, input.Truncated, model, CvAnalysisPrompt.Version);
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
