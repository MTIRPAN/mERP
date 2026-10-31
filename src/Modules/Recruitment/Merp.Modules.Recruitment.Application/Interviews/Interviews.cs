using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Settings;
using Merp.Application.Persistence;
using Merp.Application.Persistence.Files;
using Merp.Modules.Platform.Contracts.Users;
using Merp.Modules.Recruitment.Application.Applications;
using Merp.Modules.Recruitment.Application.Requisitions;
using Merp.Modules.Recruitment.Application.Settings;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Interviews;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Recruitment.Application.Interviews;

/// <summary>A user on an interview.</summary>
public sealed record InterviewPerson(Guid UserId, string? Name);

/// <summary>An interview in a list or on the calendar.</summary>
/// <remarks><c>MyFeedbackSent</c>: the caller interviews in it and sent their form.</remarks>
public sealed record InterviewListItem(
    Guid Id,
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateName,
    Guid OpeningId,
    string OpeningTitle,
    Guid CompanyId,
    InterviewType Type,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Location,
    string? OnlineLink,
    InterviewStatus Status,
    IReadOnlyList<InterviewPerson> Interviewers,
    int FeedbackCount,
    bool MyFeedbackSent);

/// <summary>A criterion's score with the criterion's name.</summary>
public sealed record FeedbackScoreItem(Guid CriterionId, string? Criterion, int Score, string? Comment);

/// <summary>An open question's answer with the question.</summary>
public sealed record FeedbackAnswerItem(Guid QuestionId, string? Question, string Text);

/// <summary>An interviewer's form.</summary>
public sealed record FeedbackItem(
    Guid Id,
    Guid InterviewId,
    Guid InterviewerUserId,
    string? InterviewerName,
    Recommendation Recommendation,
    decimal WeightedScore,
    IReadOnlyList<FeedbackScoreItem> Scores,
    IReadOnlyList<FeedbackAnswerItem> Answers,
    string? Summary,
    DateTimeOffset SubmittedAt);

/// <summary>
/// An interview with its form and the forms the caller may see (D52). With the blind rule an interviewer sees only their
/// own form until they send it (<c>FeedbackHidden</c>); HR and the hiring manager see every form and the application's
/// weighted score.
/// </summary>
public sealed record InterviewDetails(
    InterviewListItem Interview,
    string? Notes,
    string? CancelReason,
    InterviewPerson Organizer,
    EvaluationForm? Evaluation,
    FeedbackItem? MyFeedback,
    IReadOnlyList<FeedbackItem> Feedback,
    bool FeedbackHidden,
    decimal? ApplicationScore,
    string? CvFileName,
    bool CanManage,
    bool CanSubmitFeedback,
    uint Version)
{
    /// <summary>The forms of the application still missing before their summary can be asked for (D58).</summary>
    public int FeedbackPending { get; init; }
}

/// <summary>Another interview of an interviewer at the same time.</summary>
public sealed record InterviewConflict(Guid UserId, string? Name, Guid InterviewId, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <summary>An active evaluation template to plan an interview with.</summary>
public sealed record InterviewTemplateOption(Guid Id, string Name);

/// <summary>What the interview form gives.</summary>
public interface IInterviewFields
{
    InterviewType Type { get; }

    DateTimeOffset StartsAt { get; }

    DateTimeOffset EndsAt { get; }

    string? Location { get; }

    string? OnlineLink { get; }

    IReadOnlyList<Guid>? InterviewerUserIds { get; }

    string? Notes { get; }
}

/// <summary>
/// Interviews the caller sees, soonest first: of the companies in scope, and the ones the caller interviews in; with
/// <c>Mine</c> only the latter.
/// </summary>
public sealed record GetInterviewsQuery(
    PageRequest Paging,
    Guid? ApplicationId = null,
    Guid? OpeningId = null,
    bool Mine = false,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    IReadOnlyList<InterviewStatus>? Statuses = null) : IQuery<PagedResult<InterviewListItem>>, IRequirePermission
{
    public const string DefaultSort = "startsAt";

    internal static readonly SortMap<Interview> Sorts = SortMap<Interview>.By(i => i.Id)
        .Add("startsAt", i => i.StartsAt);

    public string Permission => RecruitmentPermissions.Interviews.Read;
}

/// <summary>An interview with its form and the forms the caller may see.</summary>
public sealed record GetInterviewQuery(Guid Id) : IQuery<InterviewDetails>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Interviews.Read;
}

/// <summary>The candidate's CV for the interviewers.</summary>
public sealed record GetInterviewCvQuery(Guid Id) : IQuery<RecruitmentFileDownload>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Interviews.Read;
}

/// <summary>The interviewers' other planned interviews between <c>StartsAt</c> and <c>EndsAt</c> (D52).</summary>
public sealed record GetInterviewConflictsQuery(DateTimeOffset StartsAt, DateTimeOffset EndsAt, IReadOnlyList<Guid> InterviewerUserIds, Guid? ExceptInterviewId = null)
    : IQuery<IReadOnlyList<InterviewConflict>>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Interviews.Manage;
}

/// <summary>Active users by name to interview (at most 20).</summary>
public sealed record SearchInterviewUsersQuery(string? Search = null) : IQuery<IReadOnlyList<RequisitionUserOption>>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Interviews.Manage;
}

/// <summary>The active evaluation templates to plan an interview with.</summary>
public sealed record GetInterviewTemplatesQuery : IQuery<IReadOnlyList<InterviewTemplateOption>>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Interviews.Manage;
}

/// <summary>Plans an interview of an application in the pipeline and invites the candidate and the interviewers.</summary>
public sealed record ScheduleInterviewCommand(
    Guid ApplicationId,
    InterviewType Type,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Location,
    string? OnlineLink,
    IReadOnlyList<Guid>? InterviewerUserIds,
    string? Notes,
    Guid? EvaluationTemplateId) : ICommand<Guid>, IRequirePermission, IInterviewFields
{
    /// <summary>A personalized subject of the candidate's invitation (D58); with <see cref="InvitationBody"/> it replaces the template's text.</summary>
    public string? InvitationSubject { get; init; }

    /// <summary>A personalized plain text body with the placeholders of the invitation e-mail.</summary>
    public string? InvitationBody { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Interviews.Manage;
}

/// <summary>Changes a planned interview; everyone gets the updated invitation, removed interviewers its cancellation.</summary>
public sealed record UpdateInterviewCommand(
    uint Version,
    InterviewType Type,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Location,
    string? OnlineLink,
    IReadOnlyList<Guid>? InterviewerUserIds,
    string? Notes) : ICommand, IRequirePermission, IInterviewFields
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Interviews.Manage;
}

/// <summary>Cancels a planned interview; everyone gets the cancellation.</summary>
public sealed record CancelInterviewCommand(uint Version, string? Reason) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Interviews.Manage;
}

/// <summary>Closes a planned interview: it took place, or the candidate did not come.</summary>
public sealed record CloseInterviewCommand(uint Version, InterviewStatus Status) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Interviews.Manage;
}

/// <summary>Sends or corrects the caller's form for an interview they interview in.</summary>
public sealed record SubmitFeedbackCommand(
    IReadOnlyList<CriterionScore>? Scores,
    IReadOnlyList<QuestionAnswer>? Answers,
    Recommendation Recommendation,
    string? Summary) : ICommand<Guid>, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid InterviewId { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Feedback.Submit;
}

/// <summary>Sends the reminders a day and an hour before the interviews (the reminders job, D52); returns how many.</summary>
public sealed record SendInterviewRemindersCommand : ICommand<int>, IBulkCommand;

public static class InterviewFieldErrors
{
    public const string UnknownUserCode = "recruitment.interviews.errors.unknown_user";
    public const string UnknownTemplateCode = "recruitment.interviews.errors.unknown_template";
    public const string InvalidLinkCode = "recruitment.interviews.errors.invalid_link";
}

/// <summary>Who reaches an interview and its forms (D52).</summary>
public sealed class InterviewAccess(IRecruitmentDbContext db, IAccessScopeProvider scopes, ICurrentUser currentUser, ISettings settings)
{
    /// <summary>
    /// The companies whose interviews the caller reads (null: all). Any scope of the read permission, the Self scope of
    /// the Interviewer role included, also reads the interviews the caller interviews in.
    /// </summary>
    public async Task<IReadOnlySet<Guid>?> CompaniesAsync(CancellationToken cancellationToken)
    {
        var list = await scopes.GetCurrentUserScopesAsync(RecruitmentPermissions.Interviews.Read, cancellationToken);
        return list.Any(s => s.Type == AccessScopeTypes.Tenant)
            ? null
            : list.Where(s => s.Type == AccessScopeTypes.Company && s.ScopeId is not null).Select(s => s.ScopeId!.Value).ToHashSet();
    }

    /// <summary>Over the interview's company, or as one of its interviewers.</summary>
    public async Task<bool> ReadsAsync(Interview interview, CancellationToken cancellationToken) =>
        interview.IsInterviewer(currentUser.UserId) || await CompaniesAsync(cancellationToken) is not { } companies || companies.Contains(interview.CompanyId);

    public Task<bool> ManagesAsync(Guid companyId, CancellationToken cancellationToken) =>
        RequisitionScope.CoversAsync(scopes, RecruitmentPermissions.Interviews.Manage, companyId, cancellationToken);

    /// <summary>HR over the company and the opening's hiring manager see every form of the application.</summary>
    public async Task<bool> SeesAllFeedbackAsync(Interview interview, CancellationToken cancellationToken)
    {
        if (await RequisitionScope.CoversAsync(scopes, RecruitmentPermissions.Interviews.Read, interview.CompanyId, cancellationToken))
        {
            return true;
        }

        var manager = await db.JobOpenings.AsNoTracking().Where(o => o.Id == interview.OpeningId).Select(o => o.HiringManagerUserId).SingleAsync(cancellationToken);
        return manager == currentUser.UserId;
    }

    /// <summary>With the blind rule on, an interviewer sees the application's other forms after sending their own.</summary>
    public async Task<bool> BlindAsync(Interview interview, CancellationToken cancellationToken) =>
        await settings.GetAsync<bool>(RecruitmentSettingDefinitions.BlindFeedback, interview.CompanyId, cancellationToken)
        && !await db.InterviewFeedbacks.AnyAsync(f => f.ApplicationId == interview.ApplicationId && f.InterviewerUserId == currentUser.UserId, cancellationToken);
}

/// <summary>Builds list rows and forms of interviews with names.</summary>
public sealed class InterviewRows(IRecruitmentDbContext db, IUserDirectory users, ICurrentUser currentUser)
{
    public async Task<List<InterviewListItem>> ItemsAsync(IReadOnlyList<Interview> interviews, CancellationToken cancellationToken)
    {
        var candidateIds = interviews.Select(i => i.CandidateId).Distinct().ToList();
        var candidates = await db.Candidates.AsNoTracking().Where(c => candidateIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.FullName, cancellationToken);
        var openingIds = interviews.Select(i => i.OpeningId).Distinct().ToList();
        var openings = await db.JobOpenings.AsNoTracking().Where(o => openingIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Title, cancellationToken);
        var ids = interviews.Select(i => i.Id).ToList();
        var feedback = await db.InterviewFeedbacks.AsNoTracking()
            .Where(f => ids.Contains(f.InterviewId))
            .Select(f => new { f.InterviewId, f.InterviewerUserId })
            .ToListAsync(cancellationToken);
        var names = await NamesAsync(interviews.SelectMany(i => i.InterviewerUserIds), cancellationToken);
        return
        [
            .. interviews.Select(i => new InterviewListItem(
                i.Id.Value,
                i.ApplicationId.Value,
                i.CandidateId.Value,
                candidates.GetValueOrDefault(i.CandidateId) ?? string.Empty,
                i.OpeningId.Value,
                openings.GetValueOrDefault(i.OpeningId) ?? string.Empty,
                i.CompanyId,
                i.Type,
                i.StartsAt,
                i.EndsAt,
                i.Location,
                i.OnlineLink,
                i.Status,
                [.. i.InterviewerUserIds.Select(u => new InterviewPerson(u, names.GetValueOrDefault(u)))],
                feedback.Count(f => f.InterviewId == i.Id),
                feedback.Any(f => f.InterviewId == i.Id && f.InterviewerUserId == currentUser.UserId))),
        ];
    }

    /// <summary>The forms of interviews with criterion, question and interviewer names.</summary>
    public async Task<List<FeedbackItem>> FeedbackAsync(IReadOnlyList<InterviewFeedback> forms, Dictionary<InterviewId, Interview> interviews, CancellationToken cancellationToken)
    {
        var names = await NamesAsync(forms.Select(f => f.InterviewerUserId), cancellationToken);
        return
        [
            .. forms.OrderBy(f => f.SubmittedAt).Select(f =>
            {
                var form = interviews.GetValueOrDefault(f.InterviewId)?.Evaluation;
                return new FeedbackItem(
                    f.Id.Value,
                    f.InterviewId.Value,
                    f.InterviewerUserId,
                    names.GetValueOrDefault(f.InterviewerUserId),
                    f.Recommendation,
                    f.WeightedScore,
                    [.. f.Scores.Select(s => new FeedbackScoreItem(s.CriterionId, form?.Criteria.FirstOrDefault(c => c.Id == s.CriterionId)?.Name, s.Score, s.Comment))],
                    [.. f.Answers.Select(a => new FeedbackAnswerItem(a.QuestionId, form?.Questions.FirstOrDefault(q => q.Id == a.QuestionId)?.Text, a.Text))],
                    f.Summary,
                    f.SubmittedAt);
            }),
        ];
    }

    /// <summary>The average weighted score of the application's forms; null without forms.</summary>
    public static decimal? Score(IEnumerable<InterviewFeedback> forms)
    {
        var scores = forms.Select(f => f.WeightedScore).ToList();
        return scores.Count == 0 ? null : Math.Round(scores.Average(), 2);
    }

    public async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken) =>
        (await users.GetAsync([.. userIds.Distinct()], cancellationToken)).ToDictionary(u => u.Id, u => u.DisplayName);
}

/// <summary>Checks what the interview form gives: active interviewers, a web link.</summary>
internal static class InterviewRules
{
    public static async Task<Result<InterviewTerms>> CheckAsync(IInterviewFields fields, IUserDirectory users, CancellationToken cancellationToken)
    {
        var interviewers = (fields.InterviewerUserIds ?? []).Distinct().ToList();
        var active = (await users.GetAsync(interviewers, cancellationToken)).Where(u => u.IsActive).Select(u => u.Id).ToHashSet();
        if (!interviewers.All(active.Contains))
        {
            return Error.Validation([new FieldError("interviewerUserIds", InterviewFieldErrors.UnknownUserCode)]);
        }

        return new InterviewTerms(fields.Type, fields.StartsAt, fields.EndsAt, fields.Location, fields.OnlineLink, interviewers, fields.Notes);
    }

    public static void AddFieldRules<T>(AbstractValidator<T> validator)
        where T : IInterviewFields
    {
        validator.RuleFor(i => i.Type).IsInEnum();
        validator.RuleFor(i => i.StartsAt).NotEmpty();
        validator.RuleFor(i => i.EndsAt).GreaterThan(i => i.StartsAt).WithErrorCode("recruitment.interviews.errors.invalid_time");
        validator.RuleFor(i => i.Location).MaximumLength(Interview.LocationMaxLength);
        validator.RuleFor(i => i.OnlineLink).MaximumLength(Interview.LinkMaxLength)
            .Must(link => Uri.TryCreate(link?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            .WithErrorCode(InterviewFieldErrors.InvalidLinkCode)
            .When(i => !string.IsNullOrWhiteSpace(i.OnlineLink));
        validator.RuleFor(i => i.InterviewerUserIds).NotEmpty().Must(ids => ids!.Count <= Interview.MaxInterviewers).WithErrorCode("recruitment.interviews.errors.interviewers");
        validator.RuleFor(i => i.Notes).MaximumLength(Interview.NotesMaxLength);
    }
}

internal sealed class ScheduleInterviewCommandValidator : AbstractValidator<ScheduleInterviewCommand>
{
    public ScheduleInterviewCommandValidator()
    {
        RuleFor(c => c.ApplicationId).NotEmpty();
        InterviewRules.AddFieldRules(this);
        CandidateEmails.AddTextRules(this, c => c.InvitationSubject, c => c.InvitationBody, CandidateEmailKind.InterviewInvitation);
    }
}

internal sealed class UpdateInterviewCommandValidator : AbstractValidator<UpdateInterviewCommand>
{
    public UpdateInterviewCommandValidator() => InterviewRules.AddFieldRules(this);
}

internal sealed class CancelInterviewCommandValidator : AbstractValidator<CancelInterviewCommand>
{
    public CancelInterviewCommandValidator() => RuleFor(c => c.Reason).MaximumLength(Interview.ReasonMaxLength);
}

internal sealed class CloseInterviewCommandValidator : AbstractValidator<CloseInterviewCommand>
{
    public CloseInterviewCommandValidator() =>
        RuleFor(c => c.Status).Must(s => s is InterviewStatus.Completed or InterviewStatus.NoShow).WithErrorCode(CommonErrors.ValidationCode);
}

internal sealed class SubmitFeedbackCommandValidator : AbstractValidator<SubmitFeedbackCommand>
{
    public SubmitFeedbackCommandValidator()
    {
        RuleFor(c => c.Scores).NotEmpty();
        RuleForEach(c => c.Scores).ChildRules(score =>
        {
            score.RuleFor(s => s.Score).InclusiveBetween(1, EvaluationForm.Scores);
            score.RuleFor(s => s.Comment).MaximumLength(CriterionScore.CommentMaxLength);
        });
        RuleForEach(c => c.Answers).ChildRules(answer => answer.RuleFor(a => a.Text).MaximumLength(QuestionAnswer.TextMaxLength));
        RuleFor(c => c.Recommendation).IsInEnum();
        RuleFor(c => c.Summary).MaximumLength(InterviewFeedback.SummaryMaxLength);
    }
}

internal sealed class GetInterviewsQueryValidator : AbstractValidator<GetInterviewsQuery>
{
    public GetInterviewsQueryValidator()
    {
        RuleFor(q => q.Paging.Sort).Must(GetInterviewsQuery.Sorts.IsValid).WithErrorCode(CommonErrors.InvalidSort.Code).OverridePropertyName("sort");
        RuleForEach(q => q.Statuses).IsInEnum().OverridePropertyName("status");
    }
}

internal sealed class GetInterviewsQueryHandler(IRecruitmentDbContext db, InterviewAccess access, InterviewRows rows, ICurrentUser currentUser)
    : IQueryHandler<GetInterviewsQuery, PagedResult<InterviewListItem>>
{
    public async Task<Result<PagedResult<InterviewListItem>>> Handle(GetInterviewsQuery request, CancellationToken cancellationToken)
    {
        var me = currentUser.UserId ?? Guid.Empty;
        var interviews = db.Interviews.AsNoTracking();
        var companies = await access.CompaniesAsync(cancellationToken);
        if (request.Mine)
        {
            interviews = interviews.Where(i => i.InterviewerUserIds.Contains(me));
        }
        else if (companies is not null)
        {
            var ids = companies.ToList();
            interviews = interviews.Where(i => ids.Contains(i.CompanyId) || i.InterviewerUserIds.Contains(me));
        }

        if (request.ApplicationId is { } applicationId)
        {
            interviews = interviews.Where(i => i.ApplicationId == JobApplicationId.From(applicationId));
        }

        if (request.OpeningId is { } openingId)
        {
            interviews = interviews.Where(i => i.OpeningId == JobOpeningId.From(openingId));
        }

        if (request.From is { } from)
        {
            interviews = interviews.Where(i => i.EndsAt >= from);
        }

        if (request.To is { } to)
        {
            interviews = interviews.Where(i => i.StartsAt <= to);
        }

        if (request.Statuses is { Count: > 0 } statuses)
        {
            interviews = interviews.Where(i => statuses.Contains(i.Status));
        }

        var page = await GetInterviewsQuery.Sorts.Apply(interviews, request.Paging.Sort, GetInterviewsQuery.DefaultSort)
            .ToPagedResultAsync(request.Paging, cancellationToken);
        return new PagedResult<InterviewListItem>(await rows.ItemsAsync(page.Items, cancellationToken), page.Page, page.PageSize, page.TotalCount);
    }
}

internal sealed class GetInterviewQueryHandler(
    IRecruitmentDbContext db,
    InterviewAccess access,
    InterviewRows rows,
    IAccessScopeProvider scopes,
    ICurrentUser currentUser) : IQueryHandler<GetInterviewQuery, InterviewDetails>
{
    public async Task<Result<InterviewDetails>> Handle(GetInterviewQuery request, CancellationToken cancellationToken)
    {
        var id = InterviewId.From(request.Id);
        var interview = await db.Interviews.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (interview is null || !await access.ReadsAsync(interview, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        var version = await db.Interviews.AsNoTracking().Where(i => i.Id == id).Select(i => EF.Property<uint>(i, ConcurrencyVersion.PropertyName)).SingleAsync(cancellationToken);

        // The forms of the application: every interview's, so the hiring manager and HR see the whole picture
        var interviews = await db.Interviews.AsNoTracking().Where(i => i.ApplicationId == interview.ApplicationId).ToDictionaryAsync(i => i.Id, cancellationToken);
        var forms = await db.InterviewFeedbacks.AsNoTracking().Where(f => f.ApplicationId == interview.ApplicationId).ToListAsync(cancellationToken);
        var seesAll = await access.SeesAllFeedbackAsync(interview, cancellationToken);
        var hidden = !seesAll && await access.BlindAsync(interview, cancellationToken);
        var visible = seesAll || !hidden ? forms : [.. forms.Where(f => f.InterviewerUserId == currentUser.UserId)];
        var items = await rows.FeedbackAsync(visible, interviews, cancellationToken);
        var mine = items.FirstOrDefault(f => f.InterviewId == interview.Id.Value && f.InterviewerUserId == currentUser.UserId);
        var organizer = (await rows.NamesAsync([interview.OrganizerUserId], cancellationToken)).GetValueOrDefault(interview.OrganizerUserId);
        var cvFileId = await db.JobApplications.AsNoTracking().Where(a => a.Id == interview.ApplicationId).Select(a => a.CvFileId).SingleAsync(cancellationToken);
        var cvFileName = cvFileId is { } cv
            ? await db.StoredFiles.AsNoTracking().Where(f => f.Id == cv).Select(f => f.FileName).SingleOrDefaultAsync(cancellationToken)
            : null;
        var submits = (await scopes.GetCurrentUserScopesAsync(RecruitmentPermissions.Feedback.Submit, cancellationToken)).Count > 0;
        return new InterviewDetails(
            (await rows.ItemsAsync([interview], cancellationToken)).Single(),
            interview.Notes,
            interview.CancelReason,
            new InterviewPerson(interview.OrganizerUserId, organizer),
            interview.Evaluation,
            mine,
            items,
            hidden,
            seesAll ? InterviewRows.Score(forms) : null,
            cvFileName,
            await access.ManagesAsync(interview.CompanyId, cancellationToken),
            submits && interview.IsInterviewer(currentUser.UserId) && interview.Evaluation is not null
                && interview.Status is InterviewStatus.Scheduled or InterviewStatus.Completed,
            version)
        {
            FeedbackPending = FeedbackCompletion.Pending(interviews.Values, forms),
        };
    }
}

internal sealed class GetInterviewCvQueryHandler(IRecruitmentDbContext db, InterviewAccess access, IFileStore files)
    : IQueryHandler<GetInterviewCvQuery, RecruitmentFileDownload>
{
    public async Task<Result<RecruitmentFileDownload>> Handle(GetInterviewCvQuery request, CancellationToken cancellationToken)
    {
        var interview = await db.Interviews.AsNoTracking().SingleOrDefaultAsync(i => i.Id == InterviewId.From(request.Id), cancellationToken);
        if (interview is null || !await access.ReadsAsync(interview, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        var fileId = await db.JobApplications.AsNoTracking().Where(a => a.Id == interview.ApplicationId).Select(a => a.CvFileId).SingleAsync(cancellationToken);
        var file = fileId is { } cv ? await db.StoredFiles.AsNoTracking().SingleOrDefaultAsync(f => f.Id == cv, cancellationToken) : null;
        return file is null
            ? CommonErrors.NotFound
            : new RecruitmentFileDownload(file.FileName, file.ContentType, file.Length, await files.OpenReadAsync(file, cancellationToken));
    }
}

internal sealed class GetInterviewConflictsQueryHandler(IRecruitmentDbContext db, InterviewRows rows)
    : IQueryHandler<GetInterviewConflictsQuery, IReadOnlyList<InterviewConflict>>
{
    public async Task<Result<IReadOnlyList<InterviewConflict>>> Handle(GetInterviewConflictsQuery request, CancellationToken cancellationToken)
    {
        var except = request.ExceptInterviewId is { } id ? InterviewId.From(id) : (InterviewId?)null;
        var overlapping = await db.Interviews.AsNoTracking()
            .Where(i => i.Status == InterviewStatus.Scheduled && i.StartsAt < request.EndsAt && i.EndsAt > request.StartsAt && (except == null || i.Id != except))
            .ToListAsync(cancellationToken);
        var people = request.InterviewerUserIds.ToHashSet();
        var conflicts = overlapping
            .SelectMany(i => i.InterviewerUserIds.Where(people.Contains).Select(u => (User: u, Interview: i)))
            .OrderBy(c => c.Interview.StartsAt)
            .ToList();
        var names = await rows.NamesAsync(conflicts.Select(c => c.User), cancellationToken);
        return conflicts.Select(c => new InterviewConflict(c.User, names.GetValueOrDefault(c.User), c.Interview.Id.Value, c.Interview.StartsAt, c.Interview.EndsAt)).ToList();
    }
}

internal sealed class SearchInterviewUsersQueryHandler(IUserDirectory users) : IQueryHandler<SearchInterviewUsersQuery, IReadOnlyList<RequisitionUserOption>>
{
    private const int MaxResults = 20;

    public async Task<Result<IReadOnlyList<RequisitionUserOption>>> Handle(SearchInterviewUsersQuery request, CancellationToken cancellationToken) =>
        (await users.SearchAsync(request.Search, MaxResults, cancellationToken))
            .Where(u => u.IsActive)
            .Select(u => new RequisitionUserOption(u.Id, u.DisplayName, u.UserName))
            .ToList();
}

internal sealed class GetInterviewTemplatesQueryHandler(IRecruitmentDbContext db) : IQueryHandler<GetInterviewTemplatesQuery, IReadOnlyList<InterviewTemplateOption>>
{
    public async Task<Result<IReadOnlyList<InterviewTemplateOption>>> Handle(GetInterviewTemplatesQuery request, CancellationToken cancellationToken) =>
        await db.EvaluationTemplates.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => new InterviewTemplateOption(t.Id.Value, t.Name))
            .ToListAsync(cancellationToken);
}

internal sealed class ScheduleInterviewCommandHandler(
    IRecruitmentDbContext db,
    InterviewAccess access,
    InterviewInvitations invitations,
    IUserDirectory users,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<ScheduleInterviewCommand, Guid>
{
    public async Task<Result<Guid>> Handle(ScheduleInterviewCommand request, CancellationToken cancellationToken)
    {
        var application = await db.JobApplications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == JobApplicationId.From(request.ApplicationId), cancellationToken);
        if (application is null || !await access.ManagesAsync(application.CompanyId, cancellationToken))
        {
            return Error.Validation([new FieldError("applicationId", CommonErrors.NotFound.Code)]);
        }

        EvaluationForm? form = null;
        if (request.EvaluationTemplateId is { } templateId)
        {
            var template = await db.EvaluationTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.Id == EvaluationTemplateId.From(templateId) && t.IsActive, cancellationToken);
            if (template is null)
            {
                return Error.Validation([new FieldError("evaluationTemplateId", InterviewFieldErrors.UnknownTemplateCode)]);
            }

            form = template.Form;
        }

        var terms = await InterviewRules.CheckAsync(request, users, cancellationToken);
        if (terms.IsFailure)
        {
            return terms.Error;
        }

        var interview = Interview.Schedule(application, terms.Value, form, currentUser.UserId ?? Guid.Empty, timeProvider.GetUtcNow());
        if (interview.IsFailure)
        {
            return interview.Error;
        }

        db.Interviews.Add(interview.Value);
        await invitations.SendAsync(interview.Value, InvitationKind.Invite, null, CandidateEmails.Text(request.InvitationSubject, request.InvitationBody), cancellationToken);
        return interview.Value.Id.Value;
    }
}

/// <summary>Loads a planned interview to change with the manage permission over its company.</summary>
internal static class InterviewChanges
{
    public static async Task<Interview?> LoadAsync(IRecruitmentDbContext db, InterviewAccess access, Guid id, uint version, CancellationToken cancellationToken)
    {
        var interview = await db.Interviews.SingleOrDefaultAsync(i => i.Id == InterviewId.From(id), cancellationToken);
        if (interview is null || !await access.ManagesAsync(interview.CompanyId, cancellationToken))
        {
            return null;
        }

        db.Entry(interview).ExpectVersion(version);
        return interview;
    }
}

internal sealed class UpdateInterviewCommandHandler(
    IRecruitmentDbContext db,
    InterviewAccess access,
    InterviewInvitations invitations,
    IUserDirectory users,
    TimeProvider timeProvider) : ICommandHandler<UpdateInterviewCommand>
{
    public async Task<Result> Handle(UpdateInterviewCommand request, CancellationToken cancellationToken)
    {
        var interview = await InterviewChanges.LoadAsync(db, access, request.Id, request.Version, cancellationToken);
        if (interview is null)
        {
            return CommonErrors.NotFound;
        }

        var terms = await InterviewRules.CheckAsync(request, users, cancellationToken);
        if (terms.IsFailure)
        {
            return terms.Error;
        }

        var before = interview.InterviewerUserIds.ToList();
        var changed = interview.Reschedule(terms.Value, timeProvider.GetUtcNow());
        if (changed.IsFailure)
        {
            return changed;
        }

        await invitations.SendAsync(interview, InvitationKind.Update, [.. before.Except(interview.InterviewerUserIds)], null, cancellationToken);
        return Result.Success();
    }
}

internal sealed class CancelInterviewCommandHandler(IRecruitmentDbContext db, InterviewAccess access, InterviewInvitations invitations)
    : ICommandHandler<CancelInterviewCommand>
{
    public async Task<Result> Handle(CancelInterviewCommand request, CancellationToken cancellationToken)
    {
        var interview = await InterviewChanges.LoadAsync(db, access, request.Id, request.Version, cancellationToken);
        if (interview is null)
        {
            return CommonErrors.NotFound;
        }

        var cancelled = interview.Cancel(request.Reason);
        if (cancelled.IsSuccess)
        {
            await invitations.SendAsync(interview, InvitationKind.Cancel, null, null, cancellationToken);
        }

        return cancelled;
    }
}

internal sealed class CloseInterviewCommandHandler(IRecruitmentDbContext db, InterviewAccess access) : ICommandHandler<CloseInterviewCommand>
{
    public async Task<Result> Handle(CloseInterviewCommand request, CancellationToken cancellationToken)
    {
        var interview = await InterviewChanges.LoadAsync(db, access, request.Id, request.Version, cancellationToken);
        return interview is null
            ? CommonErrors.NotFound
            : request.Status == InterviewStatus.NoShow ? interview.MarkNoShow() : interview.Complete();
    }
}

internal sealed class SubmitFeedbackCommandHandler(IRecruitmentDbContext db, InterviewAccess access, ICurrentUser currentUser, TimeProvider timeProvider)
    : ICommandHandler<SubmitFeedbackCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SubmitFeedbackCommand request, CancellationToken cancellationToken)
    {
        var interview = await db.Interviews.AsNoTracking().SingleOrDefaultAsync(i => i.Id == InterviewId.From(request.InterviewId), cancellationToken);
        if (interview is null || !await access.ReadsAsync(interview, cancellationToken) || currentUser.UserId is not { } me)
        {
            return CommonErrors.NotFound;
        }

        var now = timeProvider.GetUtcNow();
        var scores = request.Scores ?? [];
        var answers = request.Answers ?? [];
        var feedback = await db.InterviewFeedbacks.SingleOrDefaultAsync(f => f.InterviewId == interview.Id && f.InterviewerUserId == me, cancellationToken);
        if (feedback is not null)
        {
            var corrected = feedback.Correct(interview, scores, answers, request.Recommendation, request.Summary, now);
            return corrected.IsFailure ? corrected.Error : feedback.Id.Value;
        }

        var submitted = InterviewFeedback.Submit(interview, me, scores, answers, request.Recommendation, request.Summary, now);
        if (submitted.IsFailure)
        {
            return submitted.Error;
        }

        db.InterviewFeedbacks.Add(submitted.Value);
        return submitted.Value.Id.Value;
    }
}

internal sealed class SendInterviewRemindersCommandHandler(IRecruitmentDbContext db, InterviewInvitations invitations, TimeProvider timeProvider)
    : ICommandHandler<SendInterviewRemindersCommand, int>
{
    private const int BatchSize = 200;

    public async Task<Result<int>> Handle(SendInterviewRemindersCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var dayAhead = now.AddDays(1);
        var hourAhead = now.AddHours(1);
        var due = await db.Interviews
            .Where(i => i.Status == InterviewStatus.Scheduled && i.StartsAt > now
                && ((i.DayReminderSentAt == null && i.StartsAt <= dayAhead) || (i.HourReminderSentAt == null && i.StartsAt <= hourAhead)))
            .OrderBy(i => i.StartsAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        foreach (var interview in due)
        {
            // One reminder per run: within the hour the hour reminder stands for the day reminder too
            if (interview.StartsAt <= hourAhead)
            {
                interview.HourReminderSent(now);
                interview.DayReminderSent(interview.DayReminderSentAt ?? now);
            }
            else
            {
                interview.DayReminderSent(now);
            }

            await invitations.SendAsync(interview, InvitationKind.Reminder, null, null, cancellationToken);
        }

        return due.Count;
    }
}
