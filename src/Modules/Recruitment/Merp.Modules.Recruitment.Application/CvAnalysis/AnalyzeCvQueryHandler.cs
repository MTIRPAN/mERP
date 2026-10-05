using Merp.Application.Abstractions.AI;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Documents;
using Merp.Application.Abstractions.Messaging;
using Merp.SharedKernel.Privacy;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Recruitment.Application.CvAnalysis;

internal sealed class AnalyzeCvQueryHandler(
    IAiService ai,
    IDocumentTextExtractor extractor,
    ICurrentUser currentUser) : IQueryHandler<AnalyzeCvQuery, CvAnalysisResponse>
{
    public async Task<Result<CvAnalysisResponse>> Handle(AnalyzeCvQuery request, CancellationToken cancellationToken)
    {
        // Checked first so a disabled tenant never has its file parsed.
        if (!ai.IsEnabled(RecruitmentAiFeatures.CvAnalysis))
        {
            return AiErrors.Disabled;
        }

        var document = extractor.Extract(request.Content, cancellationToken);
        if (document.IsFailure)
        {
            return document.Error.ForField("file");
        }

        // Data minimization (KVKK md. 4): contact details, identifiers, the name and sensitive fields never leave.
        var names = string.IsNullOrWhiteSpace(request.CandidateName)
            ? CandidateNameDetector.Detect(document.Value.Text)
            : [request.CandidateName];
        var redacted = PersonalDataRedactor.Redact(document.Value.Text, names);

        var response = await ai.GenerateAsync<CvAnalysisResult>(
            new AiRequest
            {
                Feature = RecruitmentAiFeatures.CvAnalysis,
                PromptVersion = CvAnalysisPrompt.Version,
                Instructions = CvAnalysisPrompt.Instructions,
                Input = CvAnalysisPrompt.BuildInput(redacted.Text, request.JobTitle, request.JobRequirements, currentUser.Culture),
                Effort = AiEffort.Medium,
            },
            cancellationToken);

        if (response.IsFailure)
        {
            return response.Error;
        }

        var analysis = Normalize(response.Value.Value, string.IsNullOrWhiteSpace(request.JobRequirements));
        return new CvAnalysisResponse(
            analysis,
            [.. redacted.Counts.OrderBy(c => c.Key).Select(c => new RedactionCount(KindCode(c.Key), c.Value))],
            document.Value.Truncated,
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
