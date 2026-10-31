using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Merp.Application.Abstractions.AI;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Notifications;
using Merp.Application.Abstractions.Security;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Platform.Contracts.Users;
using Merp.Modules.Recruitment.Application.Applications;
using Merp.Modules.Recruitment.Application.CvAnalysis;
using Merp.Modules.Recruitment.Application.Requisitions;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.SharedKernel.Notifications;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Recruitment.Application.Openings;

/// <summary>A screening of an opening as its page shows it.</summary>
/// <remarks><c>Running</c>: applications in the batch running now. <c>HoldReason</c>: why it was not sent last night, as an API error code.</remarks>
public sealed record OpeningScreeningItem(
    Guid Id,
    ScreeningStatus Status,
    DateTimeOffset RequestedAt,
    string? RequestedByName,
    DateTimeOffset? SubmittedAt,
    int Running,
    decimal? EstimatedCostUsd,
    string? HoldReason,
    int Succeeded,
    int Failed,
    DateTimeOffset? CompletedAt,
    bool CanCancel);

/// <summary>Where an application's CV analysis stands against the opening's current requirements (D59).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScreeningRowState>))]
public enum ScreeningRowState
{
    /// <summary>The application has no CV.</summary>
    NoCv,

    NotAnalyzed,

    /// <summary>Analyzed against the current requirements and CV.</summary>
    Current,

    /// <summary>Analyzed, but the requirements or the CV changed since.</summary>
    Outdated,

    Failed,
}

/// <summary>A requirement of the opening and what the CV shows about it.</summary>
public sealed record ScreeningCell(string Requirement, CvRequirementStatus Status, string Evidence);

/// <summary>An application in the screening table: evidence by requirement, never a score or a rank.</summary>
public sealed record ScreeningRow(
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateName,
    DateTimeOffset AppliedAt,
    string? Stage,
    ScreeningRowState State,
    DateTimeOffset? AnalyzedAt,
    IReadOnlyList<ScreeningCell> Cells);

/// <summary>
/// The batch screening of an opening (PLAN A6, D59): the last screening, how many applications wait for an analysis,
/// and the active applications in the order they came with the evidence for each requirement. There is no score
/// and no ranking; nobody is rejected automatically.
/// </summary>
/// <remarks>
/// <c>Due</c>: active applications with a CV whose analysis is missing, failed or no longer current.
/// <c>Requirements</c>: the requirements the analyses found, in order of first appearance; the table's columns.
/// <c>Enabled</c>: the tenant has the batch screening on. <c>HasRequirements</c>: the opening lists its requirements;
/// without them there is nothing to match. <c>CanRequest</c>: the caller may ask for a screening now.
/// </remarks>
public sealed record OpeningScreeningView(
    Guid OpeningId,
    string OpeningTitle,
    OpeningScreeningItem? Screening,
    int Due,
    IReadOnlyList<string> Requirements,
    IReadOnlyList<ScreeningRow> Rows,
    bool Enabled,
    bool HasRequirements,
    bool CanRequest);

/// <summary>The screening page of an opening.</summary>
public sealed record GetOpeningScreeningQuery(Guid OpeningId) : IQuery<OpeningScreeningView>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Applications.Read;
}

/// <summary>Asks to screen an opening's applications in the next night's batch (D59).</summary>
public sealed record RequestOpeningScreeningCommand(Guid OpeningId) : ICommand<Guid>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Cv.Analyze;
}

/// <summary>Calls off a screening that was not sent yet.</summary>
public sealed record CancelOpeningScreeningCommand(Guid OpeningId, Guid Id) : ICommand, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Cv.Analyze;
}

/// <summary>
/// The night run (D59): the next waiting or held screening not in <c>Skip</c> is sent in a batch, held or closed.
/// <c>Limit</c> is what is left of the night's applications. Null when no screening waits.
/// </summary>
public sealed record SubmitNextScreeningQuery(int Limit, IReadOnlyList<Guid> Skip) : IQuery<ScreeningRun?>;

/// <summary>The next running screening not in <c>Skip</c>: its results are stored once the batch ended. Null when none runs.</summary>
public sealed record CollectNextScreeningQuery(IReadOnlyList<Guid> Skip) : IQuery<ScreeningRun?>;

/// <summary>What the night run or the collection did with a screening.</summary>
/// <remarks><c>Sent</c>: applications sent in a batch (night run) or stored (collection).</remarks>
public sealed record ScreeningRun(Guid ScreeningId, int Sent);

/// <summary>Server code records a sent batch.</summary>
public sealed record MarkScreeningSubmittedCommand(Guid Id, string BatchId, IReadOnlyList<ScreeningItem> Items, string RequirementsHash, decimal EstimatedCostUsd) : ICommand;

/// <summary>Server code records that a screening could not be sent tonight.</summary>
public sealed record HoldScreeningCommand(Guid Id, string Reason) : ICommand;

/// <summary>Server code closes a screening with nothing (more) to analyze.</summary>
public sealed record FinishScreeningCommand(Guid Id) : ICommand;

/// <summary>Server code records that the results of a screening's batch were stored.</summary>
public sealed record CollectScreeningCommand(Guid Id, int Succeeded, int Failed) : ICommand;

/// <summary>The requester hears when a screening is done.</summary>
public sealed class ScreeningNotificationTypes : INotificationTypeProvider
{
    public const string Completed = "recruitment.screening_completed";

    public IEnumerable<NotificationTypeDefinition> GetNotificationTypes() =>
    [
        new(Completed, [NotificationChannel.InApp]),
    ];
}

/// <summary>Which applications a screening sends (D59).</summary>
internal static class ScreeningSelection
{
    /// <summary>A batch's inputs together stay well under the provider's 256 MB request limit.</summary>
    public const long MaxBatchCharacters = 100_000_000;

    /// <summary>
    /// Active applications of the opening with a CV whose analysis is missing, failed or no longer current (another CV,
    /// or other requirements). Those analyzed since <paramref name="triedSince"/> are left out: they were tried in this
    /// screening already, so a CV that cannot be read is not sent every night.
    /// </summary>
    public static IQueryable<JobApplication> Due(IRecruitmentDbContext db, JobOpeningId openingId, string requirementsHash, DateTimeOffset? triedSince)
    {
        var analyses = db.CvAnalyses.AsNoTracking();
        var applications = db.JobApplications.AsNoTracking()
            .Where(a => a.OpeningId == openingId && a.Status == ApplicationStatus.Active && a.CvFileId != null)
            .Where(a => !analyses.Any(c => c.Id == a.Id
                && c.Result != null
                && c.FileId == a.CvFileId
                && (c.RequirementsHash == null || c.RequirementsHash == requirementsHash)));
        if (triedSince is { } since)
        {
            applications = applications.Where(a => !analyses.Any(c => c.Id == a.Id && c.AnalyzedAt >= since));
        }

        return applications;
    }

    public static string RequesterLink(JobOpeningId openingId) => $"/recruitment/openings/{openingId.Value}/screening";
}

internal sealed class GetOpeningScreeningQueryHandler(
    IRecruitmentDbContext db,
    IAiService ai,
    ApplicationAccess access,
    IAccessScopeProvider scopes,
    IUserDirectory users,
    ICurrentUser currentUser) : IQueryHandler<GetOpeningScreeningQuery, OpeningScreeningView>
{
    public async Task<Result<OpeningScreeningView>> Handle(GetOpeningScreeningQuery request, CancellationToken cancellationToken)
    {
        var opening = await db.JobOpenings.AsNoTracking().SingleOrDefaultAsync(o => o.Id == JobOpeningId.From(request.OpeningId), cancellationToken);
        if (opening is null || !await access.ReadsAsync(opening, asTeam: false, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        var screening = await db.OpeningScreenings.AsNoTracking()
            .Where(s => s.OpeningId == opening.Id)
            .OrderByDescending(s => s.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var rows = await db.JobApplications.AsNoTracking()
            .Where(a => a.OpeningId == opening.Id && a.Status == ApplicationStatus.Active)
            .Join(db.Candidates.AsNoTracking(), a => a.CandidateId, c => c.Id, (a, c) => new { a.Id, a.CandidateId, c.FirstName, c.LastName, a.AppliedAt, a.StageId, a.CvFileId })
            .OrderBy(r => r.AppliedAt)
            .ToListAsync(cancellationToken);
        var ids = rows.Select(r => r.Id).ToList();
        var analyses = await db.CvAnalyses.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
        var hash = ApplicationCvAnalyzer.RequirementsHash(opening);
        var culture = currentUser.Culture;

        var requirements = new List<string>();
        var screened = new List<ScreeningRow>(rows.Count);
        foreach (var row in rows)
        {
            var analysis = analyses.GetValueOrDefault(row.Id);
            var state = row.CvFileId is null ? ScreeningRowState.NoCv
                : analysis is null ? ScreeningRowState.NotAnalyzed
                : !analysis.Succeeded ? ScreeningRowState.Failed
                : analysis.FileId != row.CvFileId || (analysis.RequirementsHash is not null && analysis.RequirementsHash != hash) ? ScreeningRowState.Outdated
                : ScreeningRowState.Current;

            // Only the evidence by requirement is shown: the score and the recommendation stay on the application card
            IReadOnlyList<ScreeningCell> cells = analysis?.Result is { } json
                ? [.. (JsonSerializer.Deserialize<CvAnalysisResponse>(json, JsonSerializerOptions.Web)?.Analysis.Match?.Requirements ?? [])
                    .Where(r => !string.IsNullOrWhiteSpace(r.Requirement))
                    .Select(r => new ScreeningCell(r.Requirement.Trim(), r.Status, r.Evidence))]
                : [];
            requirements.AddRange(cells.Select(c => c.Requirement).Where(r => !requirements.Contains(r, StringComparer.Ordinal)));
            screened.Add(new ScreeningRow(
                row.Id.Value,
                row.CandidateId.Value,
                $"{row.FirstName} {row.LastName}",
                row.AppliedAt,
                opening.Stages.FirstOrDefault(s => s.Id == row.StageId)?.Name.Get(culture, "tr-TR"),
                state,
                analysis?.AnalyzedAt,
                cells));
        }

        var enabled = ai.IsEnabled(RecruitmentAiFeatures.BatchScreening);
        var hasRequirements = !string.IsNullOrWhiteSpace(RichText.ToPlainText(opening.Qualifications));
        var analyzes = await RequisitionScope.CoversAsync(scopes, RecruitmentPermissions.Cv.Analyze, opening.CompanyId, cancellationToken);
        OpeningScreeningItem? item = null;
        if (screening is not null)
        {
            var requester = await users.GetAsync([screening.RequestedByUserId], cancellationToken) is [var user, ..] ? user.DisplayName : null;
            item = new OpeningScreeningItem(
                screening.Id.Value,
                screening.Status,
                screening.RequestedAt,
                requester,
                screening.SubmittedAt,
                screening.Items.Count,
                screening.EstimatedCostUsd,
                screening.HoldReason,
                screening.SucceededCount,
                screening.FailedCount,
                screening.CompletedAt,
                analyzes && screening.Status is ScreeningStatus.Queued or ScreeningStatus.Held);
        }

        return new OpeningScreeningView(
            opening.Id.Value,
            opening.Title,
            item,
            screened.Count(r => r.State is ScreeningRowState.NotAnalyzed or ScreeningRowState.Outdated or ScreeningRowState.Failed),
            requirements,
            screened,
            enabled,
            hasRequirements,
            enabled && hasRequirements && analyzes && screening?.IsOpen != true);
    }
}

internal sealed class RequestOpeningScreeningCommandHandler(
    IRecruitmentDbContext db,
    IAiService ai,
    ApplicationAccess access,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<RequestOpeningScreeningCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RequestOpeningScreeningCommand request, CancellationToken cancellationToken)
    {
        if (!ai.IsEnabled(RecruitmentAiFeatures.BatchScreening))
        {
            return AiErrors.Disabled;
        }

        var opening = await db.JobOpenings.AsNoTracking().SingleOrDefaultAsync(o => o.Id == JobOpeningId.From(request.OpeningId), cancellationToken);
        if (opening is null || !await access.ReadsAsync(opening, asTeam: false, cancellationToken) || currentUser.UserId is not { } userId)
        {
            return CommonErrors.NotFound;
        }

        if (string.IsNullOrWhiteSpace(RichText.ToPlainText(opening.Qualifications)))
        {
            return OpeningScreeningErrors.NoRequirements;
        }

        if (await db.OpeningScreenings.AnyAsync(
                s => s.OpeningId == opening.Id && (s.Status == ScreeningStatus.Queued || s.Status == ScreeningStatus.Submitted || s.Status == ScreeningStatus.Held),
                cancellationToken))
        {
            return OpeningScreeningErrors.AlreadyOpen;
        }

        var screening = OpeningScreening.Request(opening, userId, timeProvider.GetUtcNow());
        db.OpeningScreenings.Add(screening);
        return screening.Id.Value;
    }
}

internal sealed class CancelOpeningScreeningCommandHandler(
    IRecruitmentDbContext db,
    ApplicationAccess access,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<CancelOpeningScreeningCommand>
{
    public async Task<Result> Handle(CancelOpeningScreeningCommand request, CancellationToken cancellationToken)
    {
        var screening = await db.OpeningScreenings.SingleOrDefaultAsync(
            s => s.Id == OpeningScreeningId.From(request.Id) && s.OpeningId == JobOpeningId.From(request.OpeningId),
            cancellationToken);
        var opening = screening is null ? null : await db.JobOpenings.AsNoTracking().SingleAsync(o => o.Id == screening.OpeningId, cancellationToken);
        if (screening is null || opening is null || !await access.ReadsAsync(opening, asTeam: false, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        return screening.Cancel(currentUser.UserId ?? Guid.Empty, timeProvider.GetUtcNow());
    }
}

/// <summary>
/// Prepares and sends one screening. The AI call runs outside a database transaction (D51); short commands store what
/// happened. A CV that cannot be read is stored as a failed analysis right away and is not sent.
/// </summary>
internal sealed class SubmitNextScreeningQueryHandler(
    IRecruitmentDbContext db,
    IAiService ai,
    ApplicationCvAnalyzer analyzer,
    ISender sender) : IQueryHandler<SubmitNextScreeningQuery, ScreeningRun?>
{
    /// <summary>A CV analysis usually takes about this many output tokens; the budget estimate uses it.</summary>
    private const int ExpectedOutputTokens = 5_000;

    public async Task<Result<ScreeningRun?>> Handle(SubmitNextScreeningQuery request, CancellationToken cancellationToken)
    {
        var skip = request.Skip.Select(OpeningScreeningId.From).ToList();
        var screening = await db.OpeningScreenings.AsNoTracking()
            .Where(s => (s.Status == ScreeningStatus.Queued || s.Status == ScreeningStatus.Held) && !skip.Contains(s.Id))
            .OrderBy(s => s.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (screening is null)
        {
            return Result.Success<ScreeningRun?>(null);
        }

        var id = screening.Id.Value;
        if (!ai.IsEnabled(RecruitmentAiFeatures.BatchScreening))
        {
            return await RunAsync(new HoldScreeningCommand(id, AiErrors.Disabled.Code), new ScreeningRun(id, 0), cancellationToken);
        }

        if (request.Limit <= 0)
        {
            // The night's applications are used up; it waits as it is
            return new ScreeningRun(id, 0);
        }

        var opening = await db.JobOpenings.AsNoTracking().SingleAsync(o => o.Id == screening.OpeningId, cancellationToken);
        var hash = ApplicationCvAnalyzer.RequirementsHash(opening);
        var due = await ScreeningSelection.Due(db, opening.Id, hash, screening.RequestedAt)
            .OrderBy(a => a.AppliedAt)
            .Take(request.Limit)
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return await RunAsync(new FinishScreeningCommand(id), new ScreeningRun(id, 0), cancellationToken);
        }

        var items = new List<ScreeningItem>();
        var inputs = new List<AiBatchItem>();
        long characters = 0;
        foreach (var application in due)
        {
            var prepared = await analyzer.PrepareAsync(application, cancellationToken);
            if (prepared.IsFailure)
            {
                var failed = await sender.Send(
                    new SaveApplicationCvAnalysisCommand(application.Id.Value, application.CvFileId!.Value, null, ApplicationCvAnalyzer.ErrorCode(prepared.Error)) { RequirementsHash = hash },
                    cancellationToken);
                if (failed.IsFailure)
                {
                    return failed.Error;
                }

                continue;
            }

            characters += prepared.Value.Input.Input.Length;
            if (characters > ScreeningSelection.MaxBatchCharacters && inputs.Count > 0)
            {
                // The rest goes the next night
                break;
            }

            var input = prepared.Value.Input;
            items.Add(new ScreeningItem(application.Id.Value, prepared.Value.FileId, input.Redactions.ToDictionary(r => r.Kind, r => r.Count, StringComparer.Ordinal), input.Truncated));
            inputs.Add(new AiBatchItem(application.Id.Value.ToString("N", CultureInfo.InvariantCulture), input.Input));
        }

        if (inputs.Count == 0)
        {
            return await RunAsync(new FinishScreeningCommand(id), new ScreeningRun(id, 0), cancellationToken);
        }

        var batch = await ai.SubmitBatchAsync<CvAnalysisResult>(
            new AiBatchRequest
            {
                Feature = RecruitmentAiFeatures.BatchScreening,
                PromptVersion = CvAnalysisPrompt.Version,
                Instructions = CvAnalysisPrompt.Instructions,
                Items = inputs,
                Effort = AiEffort.Medium,
                ExpectedOutputTokens = ExpectedOutputTokens,
                EntityType = ScreeningBatches.EntityType,
            },
            cancellationToken);
        return batch.IsFailure
            ? await RunAsync(new HoldScreeningCommand(id, batch.Error.Code), new ScreeningRun(id, 0), cancellationToken)
            : await RunAsync(new MarkScreeningSubmittedCommand(id, batch.Value.Id, items, hash, batch.Value.EstimatedCostUsd), new ScreeningRun(id, items.Count), cancellationToken);
    }

    private async Task<Result<ScreeningRun?>> RunAsync(ICommand command, ScreeningRun run, CancellationToken cancellationToken)
    {
        var sent = await sender.Send(command, cancellationToken);
        return sent.IsFailure ? sent.Error : run;
    }
}

/// <summary>What a screening's batch is sent as.</summary>
internal static class ScreeningBatches
{
    public const string EntityType = "recruitment.application";

    public static AiBatch Of(OpeningScreening screening) =>
        new(screening.BatchId!, RecruitmentAiFeatures.BatchScreening, CvAnalysisPrompt.Version, EntityType, screening.EstimatedCostUsd ?? 0);
}

/// <summary>Stores the results of one screening's batch once it ended, each as the application's CV analysis.</summary>
internal sealed class CollectNextScreeningQueryHandler(IRecruitmentDbContext db, IAiService ai, ISender sender)
    : IQueryHandler<CollectNextScreeningQuery, ScreeningRun?>
{
    public async Task<Result<ScreeningRun?>> Handle(CollectNextScreeningQuery request, CancellationToken cancellationToken)
    {
        var skip = request.Skip.Select(OpeningScreeningId.From).ToList();
        var screening = await db.OpeningScreenings.AsNoTracking()
            .Where(s => s.Status == ScreeningStatus.Submitted && !skip.Contains(s.Id))
            .OrderBy(s => s.SubmittedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (screening is null)
        {
            return Result.Success<ScreeningRun?>(null);
        }

        var id = screening.Id.Value;
        var results = await ai.GetBatchResultsAsync<CvAnalysisResult>(ScreeningBatches.Of(screening), cancellationToken);
        if (results.IsFailure || results.Value is not { } ended)
        {
            // Still running, or the provider is unreachable now: the next run asks again
            return new ScreeningRun(id, 0);
        }

        // An application deleted or anonymized meanwhile gets no analysis
        var applicationIds = screening.Items.Select(i => JobApplicationId.From(i.ApplicationId)).ToList();
        var present = (await db.JobApplications.AsNoTracking()
                .Where(a => applicationIds.Contains(a.Id) && a.CvFileId != null)
                .Select(a => a.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var byId = ended.ToDictionary(r => r.CustomId, StringComparer.OrdinalIgnoreCase);
        int succeeded = 0, failed = 0;
        foreach (var item in screening.Items.Where(i => present.Contains(JobApplicationId.From(i.ApplicationId))))
        {
            var result = byId.GetValueOrDefault(item.ApplicationId.ToString("N", CultureInfo.InvariantCulture))?.Response ?? AiErrors.Unavailable;
            var input = new CvAnalysisInput(
                string.Empty,
                [.. item.Redactions.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => new RedactionCount(r.Key, r.Value))],
                item.Truncated,
                WithoutRequirements: false);
            var response = result.IsSuccess ? CvAnalysisRun.Respond(result.Value.Value, result.Value.Usage.Model, input) : null;
            var saved = await sender.Send(
                new SaveApplicationCvAnalysisCommand(item.ApplicationId, item.FileId, response, response is null ? ApplicationCvAnalyzer.ErrorCode(result.Error) : null)
                {
                    RequirementsHash = screening.RequirementsHash,
                },
                cancellationToken);
            if (saved.IsFailure)
            {
                return saved.Error;
            }

            if (response is null)
            {
                failed++;
            }
            else
            {
                succeeded++;
            }
        }

        var collected = await sender.Send(new CollectScreeningCommand(id, succeeded, failed), cancellationToken);
        return collected.IsFailure ? collected.Error : new ScreeningRun(id, succeeded + failed);
    }
}

internal sealed class MarkScreeningSubmittedCommandHandler(IRecruitmentDbContext db, TimeProvider timeProvider) : ICommandHandler<MarkScreeningSubmittedCommand>
{
    public async Task<Result> Handle(MarkScreeningSubmittedCommand request, CancellationToken cancellationToken)
    {
        var screening = await db.OpeningScreenings.SingleAsync(s => s.Id == OpeningScreeningId.From(request.Id), cancellationToken);
        return screening.Submit(request.BatchId, request.Items, request.RequirementsHash, request.EstimatedCostUsd, timeProvider.GetUtcNow());
    }
}

internal sealed class HoldScreeningCommandHandler(IRecruitmentDbContext db) : ICommandHandler<HoldScreeningCommand>
{
    public async Task<Result> Handle(HoldScreeningCommand request, CancellationToken cancellationToken)
    {
        var screening = await db.OpeningScreenings.SingleAsync(s => s.Id == OpeningScreeningId.From(request.Id), cancellationToken);
        return screening.Hold(request.Reason);
    }
}

internal sealed class FinishScreeningCommandHandler(IRecruitmentDbContext db, ScreeningNotifier notifier, TimeProvider timeProvider) : ICommandHandler<FinishScreeningCommand>
{
    public async Task<Result> Handle(FinishScreeningCommand request, CancellationToken cancellationToken)
    {
        var screening = await db.OpeningScreenings.SingleAsync(s => s.Id == OpeningScreeningId.From(request.Id), cancellationToken);
        var finished = screening.Finish(timeProvider.GetUtcNow());
        if (finished.IsSuccess)
        {
            await notifier.CompletedAsync(screening, cancellationToken);
        }

        return finished;
    }
}

internal sealed class CollectScreeningCommandHandler(IRecruitmentDbContext db, ScreeningNotifier notifier, TimeProvider timeProvider)
    : ICommandHandler<CollectScreeningCommand>
{
    public async Task<Result> Handle(CollectScreeningCommand request, CancellationToken cancellationToken)
    {
        var screening = await db.OpeningScreenings.SingleAsync(s => s.Id == OpeningScreeningId.From(request.Id), cancellationToken);
        var opening = await db.JobOpenings.AsNoTracking().SingleAsync(o => o.Id == screening.OpeningId, cancellationToken);

        // Applications the night's limit left out go the next night; those just stored count as tried
        var moreToDo = await ScreeningSelection.Due(db, opening.Id, ApplicationCvAnalyzer.RequirementsHash(opening), screening.RequestedAt)
            .AnyAsync(cancellationToken);
        var collected = screening.Collect(request.Succeeded, request.Failed, moreToDo, timeProvider.GetUtcNow());
        if (collected.IsSuccess && screening.Status == ScreeningStatus.Completed)
        {
            await notifier.CompletedAsync(screening, cancellationToken);
        }

        return collected;
    }
}

/// <summary>Tells the requester a screening is done, with what it analyzed.</summary>
public sealed class ScreeningNotifier(IRecruitmentDbContext db, IIntegrationEventPublisher publisher)
{
    public async Task CompletedAsync(OpeningScreening screening, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(screening);
        var title = await db.JobOpenings.AsNoTracking().Where(o => o.Id == screening.OpeningId).Select(o => o.Title).SingleAsync(cancellationToken);
        await publisher.PublishAsync(
            new NotificationRequested(
                ScreeningNotificationTypes.Completed,
                [screening.RequestedByUserId],
                new Dictionary<string, string>
                {
                    ["position"] = title,
                    ["succeeded"] = screening.SucceededCount.ToString(CultureInfo.InvariantCulture),
                    ["failed"] = screening.FailedCount.ToString(CultureInfo.InvariantCulture),
                },
                ScreeningSelection.RequesterLink(screening.OpeningId))
            {
                CompanyId = screening.CompanyId,
            },
            cancellationToken);
    }
}
