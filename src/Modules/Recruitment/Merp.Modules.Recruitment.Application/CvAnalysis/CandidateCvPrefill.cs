using FluentValidation;
using Merp.Application.Abstractions.AI;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Documents;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Recruitment.Application.CvAnalysis;

/// <summary>
/// What the candidate form can take from a CV (A3, D51). The contact details are read here; the profile comes from the
/// AI analysis when the feature is on. Every value is a suggestion the user checks before saving.
/// </summary>
/// <param name="FirstName">From the name on the first lines.</param>
/// <param name="LastName">From the name on the first lines.</param>
/// <param name="Email">The first e-mail address.</param>
/// <param name="Phone">The first phone number.</param>
/// <param name="LinkedInUrl">A LinkedIn profile link.</param>
/// <param name="PortfolioUrl">Another web link.</param>
/// <param name="CurrentTitle">The latest job title (AI).</param>
/// <param name="CurrentCompany">The latest employer (AI).</param>
/// <param name="ExperienceYears">Total experience in years (AI).</param>
/// <param name="Education">The latest education in one line (AI).</param>
/// <param name="AiUsed">The profile fields came from the AI analysis.</param>
/// <param name="AiErrorCode">Why the AI analysis did not run or failed; null when it ran or the feature is off.</param>
public sealed record CandidateCvPrefill(
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? LinkedInUrl,
    string? PortfolioUrl,
    string? CurrentTitle,
    string? CurrentCompany,
    decimal? ExperienceYears,
    string? Education,
    bool AiUsed,
    string? AiErrorCode);

/// <summary>Reads a CV for the new candidate form (A3, D51); nothing is stored.</summary>
public sealed record PrefillCandidateFromCvQuery(ReadOnlyMemory<byte> Content) : IQuery<CandidateCvPrefill>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Candidates.Manage;
}

internal sealed class PrefillCandidateFromCvQueryValidator : AbstractValidator<PrefillCandidateFromCvQuery>
{
    public PrefillCandidateFromCvQueryValidator() =>
        RuleFor(q => q.Content.Length).GreaterThan(0).LessThanOrEqualTo(AnalyzeCvQuery.MaxFileBytes).OverridePropertyName("cv");
}

internal sealed class PrefillCandidateFromCvQueryHandler(IAiService ai, IDocumentTextExtractor extractor, ICurrentUser currentUser)
    : IQueryHandler<PrefillCandidateFromCvQuery, CandidateCvPrefill>
{
    public async Task<Result<CandidateCvPrefill>> Handle(PrefillCandidateFromCvQuery request, CancellationToken cancellationToken)
    {
        var document = extractor.Extract(request.Content, cancellationToken);
        if (document.IsFailure)
        {
            return document.Error.ForField("cv");
        }

        // The contact details are read here and never sent (PLAN A3)
        var contact = CvContactExtractor.Extract(document.Value.Text);
        var local = new CandidateCvPrefill(contact.FirstName, contact.LastName, contact.Email, contact.Phone, contact.LinkedInUrl, contact.PortfolioUrl, null, null, null, null, false, null);
        if (!ai.IsEnabled(RecruitmentAiFeatures.CvAnalysis))
        {
            return local;
        }

        string[] names = contact.FirstName is null ? [] : [$"{contact.FirstName} {contact.LastName}"];
        var analysis = await CvAnalysisRun.AnalyzeAsync(ai, document.Value, names, null, null, currentUser.Culture, cancellationToken);
        if (analysis.IsFailure)
        {
            // The form still gets the contact details; the user sees why the rest is missing
            return local with { AiErrorCode = analysis.Error.Code };
        }

        var result = analysis.Value.Analysis;
        var latestJob = result.Experiences.FirstOrDefault(e => e.IsCurrent) ?? (result.Experiences.Count > 0 ? result.Experiences[0] : null);
        var latestSchool = result.Education.Count > 0 ? result.Education[0] : null;
        return local with
        {
            CurrentTitle = Clip(Blank(result.CurrentTitle) ?? Blank(latestJob?.Title), CandidateProfile.TextMaxLength),
            CurrentCompany = Clip(Blank(latestJob?.Company), CandidateProfile.TextMaxLength),
            ExperienceYears = result.TotalExperienceYears,
            Education = latestSchool is null
                ? null
                : Clip(string.Join(", ", new[] { latestSchool.Degree, latestSchool.Field, latestSchool.Institution }.Where(s => !string.IsNullOrWhiteSpace(s))), CandidateProfile.EducationMaxLength),
            AiUsed = true,
        };
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // The form's limits; the model may write longer values
    private static string? Clip(string? value, int maxLength) => value is { Length: > 0 } text ? (text.Length > maxLength ? text[..maxLength] : text) : null;
}
