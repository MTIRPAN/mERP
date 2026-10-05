using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;

namespace Merp.Modules.Recruitment.Application.CvAnalysis;

/// <summary>
/// Analyzes an uploaded CV with AI and, when requirements are given, assesses the match (D24). Nothing is stored:
/// the file is read in memory, personal data is redacted before it leaves the system, and only usage figures are
/// recorded.
/// </summary>
/// <param name="FileName">Original file name; only its extension is checked.</param>
/// <param name="Content">File content, PDF or DOCX.</param>
/// <param name="JobTitle">Optional position title for the match.</param>
/// <param name="JobRequirements">Optional requirements to match against, one per line or free text.</param>
/// <param name="CandidateName">Optional name to redact; otherwise a name on the first lines is detected.</param>
public sealed record AnalyzeCvQuery(
    string FileName,
    ReadOnlyMemory<byte> Content,
    string? JobTitle,
    string? JobRequirements,
    string? CandidateName) : IQuery<CvAnalysisResponse>, IRequirePermission
{
    public const int MaxFileBytes = 5 * 1024 * 1024;
    public const int MaxJobTitleLength = 200;
    public const int MaxJobRequirementsLength = 8000;
    public const int MaxCandidateNameLength = 200;

    public static readonly IReadOnlyList<string> AllowedExtensions = [".pdf", ".docx"];

    public string Permission => RecruitmentPermissions.Cv.Analyze;
}

/// <param name="Analysis">The model's analysis; a suggestion for the recruiter, not a decision.</param>
/// <param name="Redactions">What was removed before the CV was sent, by kind.</param>
/// <param name="DocumentTruncated">Only the beginning of a very long document was analyzed.</param>
/// <param name="Model">Model that produced the analysis.</param>
/// <param name="PromptVersion">Version of the instructions.</param>
public sealed record CvAnalysisResponse(
    CvAnalysisResult Analysis,
    IReadOnlyList<RedactionCount> Redactions,
    bool DocumentTruncated,
    string Model,
    string PromptVersion);

/// <param name="Kind">email, phone, national_id, iban, url, name or sensitive_field.</param>
/// <param name="Count">Number of values removed.</param>
public sealed record RedactionCount(string Kind, int Count);
