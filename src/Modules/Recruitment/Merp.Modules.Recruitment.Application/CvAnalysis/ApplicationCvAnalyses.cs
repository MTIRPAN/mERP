using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Merp.Application.Abstractions.AI;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Documents;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence.Files;
using Merp.Modules.Recruitment.Application.Applications;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Recruitment.Application.CvAnalysis;

/// <summary>The AI analysis of an application's CV, as the application card shows it (A3, D51).</summary>
/// <param name="Succeeded">False when the run failed; <paramref name="ErrorCode"/> says why.</param>
/// <param name="Response">The analysis, in the shape of the stateless CV analysis (A2).</param>
/// <param name="ErrorCode">An API error code, e.g. <c>common.errors.ai_budget_exceeded</c>.</param>
/// <param name="AnalyzedAt">When it ran.</param>
/// <param name="RequestedByName">Who asked for it; null for the background job.</param>
public sealed record ApplicationCvAnalysisItem(bool Succeeded, CvAnalysisResponse? Response, string? ErrorCode, DateTimeOffset AnalyzedAt, string? RequestedByName);

/// <summary>
/// Analyzes the CV of an application against its opening's requirements with AI (A3, D51) and keeps the result on the
/// application; a previous analysis is replaced only by a successful one. The AI call runs outside a database
/// transaction; <see cref="SaveApplicationCvAnalysisCommand"/> stores the result.
/// </summary>
public sealed record AnalyzeApplicationCvQuery(Guid Id) : IQuery<ApplicationCvAnalysisItem>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Cv.Analyze;
}

/// <summary>
/// The background analysis (A3, D51): the next active application of the last 30 days with a CV and no analysis is
/// analyzed and its result stored, failures included. False when there is none or the feature is off.
/// </summary>
public sealed record AnalyzeNextApplicationCvQuery : IQuery<bool>;

/// <summary>Stores the outcome of an analysis on the application; only server code sends it.</summary>
public sealed record SaveApplicationCvAnalysisCommand(Guid ApplicationId, Guid FileId, CvAnalysisResponse? Response, string? ErrorCode) : ICommand
{
    /// <summary>The digest of the opening's requirements the CV was matched against (D59).</summary>
    public string? RequirementsHash { get; init; }
}

/// <summary>An application's CV ready to send, with the digest of the requirements it is matched against.</summary>
public sealed record PreparedCv(Guid FileId, CvAnalysisInput Input, string RequirementsHash);

public static class ApplicationCvAnalysisErrors
{
    public static readonly Error NoCv = Error.Validation([new FieldError("cv", "recruitment.applications.errors.no_cv")]);
}

/// <summary>Runs the analysis of an application's CV: the file, the names to redact, the opening's requirements.</summary>
public sealed class ApplicationCvAnalyzer(
    IRecruitmentDbContext db,
    IAiService ai,
    IDocumentTextExtractor extractor,
    IFileStore files,
    ILocalizationSource localization)
{
    /// <summary>Applications older than this are not analyzed in the background (user decision).</summary>
    public static readonly TimeSpan BackgroundWindow = TimeSpan.FromDays(30);

    public bool IsEnabled => ai.IsEnabled(RecruitmentAiFeatures.CvAnalysis);

    /// <summary>
    /// A digest of what a CV is matched against: the opening's title and requirements (D59). An analysis with another
    /// digest is no longer current.
    /// </summary>
    public static string RequirementsHash(JobOpening opening)
    {
        ArgumentNullException.ThrowIfNull(opening);
        var text = $"{opening.Title.Trim()}\n{RichText.ToPlainText(opening.Qualifications)?.Trim()}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    public async Task<Result<CvAnalysisResponse>> AnalyzeAsync(JobApplication application, CancellationToken cancellationToken)
    {
        var prepared = await PrepareAsync(application, cancellationToken);
        if (prepared.IsFailure)
        {
            return prepared.Error;
        }

        var response = await ai.GenerateAsync<CvAnalysisResult>(
            new AiRequest
            {
                Feature = RecruitmentAiFeatures.CvAnalysis,
                PromptVersion = CvAnalysisPrompt.Version,
                Instructions = CvAnalysisPrompt.Instructions,
                Input = prepared.Value.Input.Input,
                Effort = AiEffort.Medium,
            },
            cancellationToken);
        return response.IsFailure ? response.Error : CvAnalysisRun.Respond(response.Value.Value, response.Value.Usage.Model, prepared.Value.Input);
    }

    /// <summary>Reads the application's CV and builds the redacted input against its opening's requirements.</summary>
    public async Task<Result<PreparedCv>> PrepareAsync(JobApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        var file = application.CvFileId is { } fileId ? await db.StoredFiles.AsNoTracking().SingleOrDefaultAsync(f => f.Id == fileId, cancellationToken) : null;
        if (file is null)
        {
            return ApplicationCvAnalysisErrors.NoCv;
        }

        byte[] content;
        await using (var stream = await files.OpenReadAsync(file, cancellationToken))
        using (var buffer = new MemoryStream())
        {
            await stream.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        var document = extractor.Extract(content, cancellationToken);
        if (document.IsFailure)
        {
            return document.Error.ForField("cv");
        }

        var candidate = await db.Candidates.AsNoTracking().SingleAsync(c => c.Id == application.CandidateId, cancellationToken);
        var opening = await db.JobOpenings.AsNoTracking().SingleAsync(o => o.Id == application.OpeningId, cancellationToken);

        // The stored analysis is in the tenant's language, whoever runs it
        var culture = (await localization.GetAsync(cancellationToken)).DefaultCulture;
        var input = CvAnalysisRun.Prepare(
            document.Value,
            [candidate.FullName, .. CandidateNameDetector.Detect(document.Value.Text)],
            opening.Title,
            RichText.ToPlainText(opening.Qualifications),
            culture);
        return new PreparedCv(file.Id, input, RequirementsHash(opening));
    }

    /// <summary>The code kept for a failed analysis: the field's code when the error is about one (e.g. an unreadable CV).</summary>
    public static string ErrorCode(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Fields.Count > 0 ? error.Fields[0].Code : error.Code;
    }

    public static ApplicationCvAnalysisItem? Item(ApplicationCvAnalysis? analysis, string? requestedByName) =>
        analysis is null
            ? null
            : new ApplicationCvAnalysisItem(
                analysis.Succeeded,
                analysis.Result is { } json ? JsonSerializer.Deserialize<CvAnalysisResponse>(json, JsonSerializerOptions.Web) : null,
                analysis.ErrorCode,
                analysis.AnalyzedAt,
                requestedByName);
}

internal sealed class AnalyzeApplicationCvQueryHandler(
    IRecruitmentDbContext db,
    ApplicationAccess access,
    ApplicationCvAnalyzer analyzer,
    ISender sender) : IQueryHandler<AnalyzeApplicationCvQuery, ApplicationCvAnalysisItem>
{
    public async Task<Result<ApplicationCvAnalysisItem>> Handle(AnalyzeApplicationCvQuery request, CancellationToken cancellationToken)
    {
        var application = await db.JobApplications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == JobApplicationId.From(request.Id), cancellationToken);
        var opening = application is null ? null : await db.JobOpenings.AsNoTracking().SingleAsync(o => o.Id == application.OpeningId, cancellationToken);
        if (application is null || opening is null || !await access.ReadsAsync(opening, asTeam: false, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        if (!analyzer.IsEnabled)
        {
            return AiErrors.Disabled;
        }

        var analysis = await analyzer.AnalyzeAsync(application, cancellationToken);
        if (analysis.IsFailure)
        {
            // A failed rerun leaves the previous analysis as it is
            return analysis.Error;
        }

        var saved = await sender.Send(
            new SaveApplicationCvAnalysisCommand(request.Id, application.CvFileId!.Value, analysis.Value, null) { RequirementsHash = ApplicationCvAnalyzer.RequirementsHash(opening) },
            cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        var stored = await db.CvAnalyses.AsNoTracking().SingleAsync(a => a.Id == application.Id, cancellationToken);
        return ApplicationCvAnalyzer.Item(stored, null)!;
    }
}

internal sealed class AnalyzeNextApplicationCvQueryHandler(
    IRecruitmentDbContext db,
    ApplicationCvAnalyzer analyzer,
    ISender sender,
    TimeProvider timeProvider) : IQueryHandler<AnalyzeNextApplicationCvQuery, bool>
{
    public async Task<Result<bool>> Handle(AnalyzeNextApplicationCvQuery request, CancellationToken cancellationToken)
    {
        if (!analyzer.IsEnabled)
        {
            return false;
        }

        var since = timeProvider.GetUtcNow() - ApplicationCvAnalyzer.BackgroundWindow;
        var analyzed = db.CvAnalyses.Select(a => a.Id);
        var application = await db.JobApplications.AsNoTracking()
            .Where(a => a.Status == ApplicationStatus.Active && a.CvFileId != null && a.AppliedAt >= since && !analyzed.Contains(a.Id))
            .OrderBy(a => a.AppliedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (application is null)
        {
            return false;
        }

        // A failure is kept too, so the job moves on; the team runs it again from the card
        var analysis = await analyzer.AnalyzeAsync(application, cancellationToken);
        var opening = await db.JobOpenings.AsNoTracking().SingleAsync(o => o.Id == application.OpeningId, cancellationToken);
        var saved = await sender.Send(
            new SaveApplicationCvAnalysisCommand(
                application.Id.Value,
                application.CvFileId!.Value,
                analysis.IsSuccess ? analysis.Value : null,
                analysis.IsFailure ? ApplicationCvAnalyzer.ErrorCode(analysis.Error) : null)
            {
                RequirementsHash = ApplicationCvAnalyzer.RequirementsHash(opening),
            },
            cancellationToken);
        return saved.IsFailure ? saved.Error : true;
    }
}

internal sealed class SaveApplicationCvAnalysisCommandHandler(IRecruitmentDbContext db, ICurrentUser currentUser, TimeProvider timeProvider)
    : ICommandHandler<SaveApplicationCvAnalysisCommand>
{
    public async Task<Result> Handle(SaveApplicationCvAnalysisCommand request, CancellationToken cancellationToken)
    {
        var id = JobApplicationId.From(request.ApplicationId);
        var analysis = await db.CvAnalyses.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (analysis is null)
        {
            analysis = ApplicationCvAnalysis.Of(id);
            db.CvAnalyses.Add(analysis);
        }

        var userId = currentUser.IsSystem ? null : currentUser.UserId;
        var now = timeProvider.GetUtcNow();
        if (request.Response is { } response)
        {
            analysis.Succeed(request.FileId, JsonSerializer.Serialize(response, JsonSerializerOptions.Web), response.Model, response.PromptVersion, userId, now, request.RequirementsHash);
        }
        else
        {
            analysis.Fail(request.FileId, request.ErrorCode ?? CommonErrors.Unexpected.Code, userId, now, request.RequirementsHash);
        }

        return Result.Success();
    }
}
