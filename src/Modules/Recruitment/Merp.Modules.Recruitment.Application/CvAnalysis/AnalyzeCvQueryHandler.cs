using Merp.Application.Abstractions.AI;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Documents;
using Merp.Application.Abstractions.Messaging;
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

        var names = string.IsNullOrWhiteSpace(request.CandidateName)
            ? CandidateNameDetector.Detect(document.Value.Text)
            : [request.CandidateName];
        return await CvAnalysisRun.AnalyzeAsync(ai, document.Value, names, request.JobTitle, request.JobRequirements, currentUser.Culture, cancellationToken);
    }
}
