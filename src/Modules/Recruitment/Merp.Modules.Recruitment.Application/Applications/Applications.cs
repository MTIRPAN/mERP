using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence;
using Merp.Application.Persistence.Files;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.Modules.Platform.Contracts.Users;
using Merp.Modules.Recruitment.Application.CvAnalysis;
using Merp.Modules.Recruitment.Application.Interviews;
using Merp.Modules.Recruitment.Application.Openings;
using Merp.Modules.Recruitment.Application.Offers;
using Merp.Modules.Recruitment.Application.Requisitions;
using Merp.Modules.Recruitment.Application.Settings;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Offers;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Recruitment.Application.Applications;

/// <summary>A stage of an opening's pipeline with the number of applications at it.</summary>
public sealed record PipelineStage(Guid Id, string Name, StageType Type, int? SlaDays, int Count);

/// <summary>An active rejection or withdrawal reason to choose from.</summary>
public sealed record ReasonOption(string Code, string Name);

/// <summary>
/// An opening's pipeline for the kanban (D50). <c>CanManage</c>: the caller moves, rejects and reopens its
/// applications; the reasons to choose from come only then.
/// </summary>
public sealed record OpeningPipeline(
    Guid OpeningId,
    string Title,
    Guid CompanyId,
    string? CompanyName,
    OpeningStatus Status,
    IReadOnlyList<PipelineStage> Stages,
    bool CanManage,
    IReadOnlyList<ReasonOption> RejectionReasons,
    IReadOnlyList<ReasonOption> WithdrawalReasons);

/// <summary>An application in a list or on a kanban card; <c>SlaDueAt</c>: when an active application overstays its stage, null without an SLA.</summary>
public sealed record ApplicationListItem(
    Guid Id,
    Guid CandidateId,
    string CandidateName,
    string Email,
    string? Phone,
    Guid OpeningId,
    string OpeningTitle,
    Guid CompanyId,
    Guid StageId,
    string? StageName,
    StageType? StageType,
    ApplicationStatus Status,
    bool KnockedOut,
    bool IsInternal,
    string Source,
    DateTimeOffset AppliedAt,
    DateTimeOffset StageEnteredAt,
    DateTimeOffset? SlaDueAt,
    DateTimeOffset? HireRequestedAt);

/// <summary>An answer with its question; <paramref name="Missed"/> marks a knockout answer outside the accepted ones.</summary>
public sealed record ApplicationAnswerItem(Guid QuestionId, string? Question, QuestionType? Type, IReadOnlyList<string> Values, bool Missed);

/// <summary>The CV sent with the application.</summary>
public sealed record ApplicationCvItem(string FileName, string ContentType, long Length);

/// <summary>A step of the application through the pipeline, with names in the caller's language.</summary>
public sealed record ApplicationStageChangeItem(
    Guid? FromStageId,
    string? FromStageName,
    Guid ToStageId,
    string? ToStageName,
    ApplicationStatus Status,
    string? ReasonCode,
    string? ReasonName,
    string? Note,
    Guid? UserId,
    string? UserName,
    DateTimeOffset At);

/// <summary>
/// An application with its answers, CV, consent and history (D50), the AI analysis of its CV (D51), its interviews and
/// every interviewer's form with the weighted score (D52): those who read the application, HR and the hiring team, see
/// all forms; the blind rule binds interviewers only.
/// </summary>
public sealed record ApplicationDetails(
    ApplicationListItem Application,
    string? CoverLetter,
    IReadOnlyList<ApplicationAnswerItem> Answers,
    ApplicationCvItem? Cv,
    ApplicationConsent? Consent,
    string? ReasonCode,
    string? ReasonName,
    DateTimeOffset? RejectionEmailDueAt,
    DateTimeOffset? RejectionEmailSentAt,
    IReadOnlyList<ApplicationStageChangeItem> History,
    bool CanManage,
    uint Version,
    ApplicationCvAnalysisItem? CvAnalysis,
    bool CanAnalyzeCv,
    IReadOnlyList<InterviewListItem> Interviews,
    IReadOnlyList<FeedbackItem> Feedback,
    decimal? Score,
    bool CanScheduleInterview,
    IReadOnlyList<OfferListItem> Offers,
    bool CanCreateOffer,
    bool CanHire);

/// <summary>A file of a recruitment record to send.</summary>
public sealed record RecruitmentFileDownload(string FileName, string ContentType, long Length, Stream Content);


/// <summary>
/// The pipeline of an opening with the number of applications at each stage. The queries of applications run from
/// self service too (D50): <c>AsTeam</c> switches the permission and limits them to the openings of the caller's team.
/// </summary>
public sealed record GetOpeningPipelineQuery(Guid Id) : IQuery<OpeningPipeline>, IRequirePermission
{
    [JsonIgnore]
    public bool AsTeam { get; init; }

    public string Permission => AsTeam ? RecruitmentPermissions.Self.Hiring : RecruitmentPermissions.Applications.Read;
}

/// <summary>Applications to the openings of the companies in scope, newest first unless sorted; a team member's of one opening.</summary>
public sealed record GetApplicationsQuery(
    PageRequest Paging,
    Guid? OpeningId = null,
    Guid? StageId = null,
    IReadOnlyList<ApplicationStatus>? Statuses = null,
    bool? KnockedOut = null,
    string? Search = null) : IQuery<PagedResult<ApplicationListItem>>, IRequirePermission
{
    public const string DefaultSort = "-appliedAt";

    internal static readonly SortMap<ApplicationRow> Sorts = SortMap<ApplicationRow>.By(r => r.Id)
        .Add("appliedAt", r => r.AppliedAt)
        .Add("stageEnteredAt", r => r.StageEnteredAt)
        .Add("name", r => r.FirstName);

    [JsonIgnore]
    public bool AsTeam { get; init; }

    public string Permission => AsTeam ? RecruitmentPermissions.Self.Hiring : RecruitmentPermissions.Applications.Read;
}

/// <summary>An application with its answers, CV, consent and history.</summary>
public sealed record GetApplicationQuery(Guid Id) : IQuery<ApplicationDetails>, IRequirePermission
{
    [JsonIgnore]
    public bool AsTeam { get; init; }

    public string Permission => AsTeam ? RecruitmentPermissions.Self.Hiring : RecruitmentPermissions.Applications.Read;
}

/// <summary>The CV sent with an application.</summary>
public sealed record GetApplicationCvQuery(Guid Id) : IQuery<RecruitmentFileDownload>, IRequirePermission
{
    [JsonIgnore]
    public bool AsTeam { get; init; }

    public string Permission => AsTeam ? RecruitmentPermissions.Self.Hiring : RecruitmentPermissions.Applications.Read;
}

/// <summary>The openings whose hiring team the caller is in (hiring manager or recruiter), not drafts.</summary>
public sealed record GetHiringOpeningsQuery : IQuery<IReadOnlyList<OpeningListItem>>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Self.Hiring;
}

/// <summary>Moves an active application to a working stage of its opening, or to Hired.</summary>
public sealed record MoveApplicationCommand(uint Version, Guid StageId, string? Note) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Applications.Manage;
}

/// <summary>Rejects an active application for a reason; the rejection e-mail goes now or up to 14 days later.</summary>
/// <param name="Version">The version read.</param>
/// <param name="ReasonCode">An active rejection reason.</param>
/// <param name="Note">Why, in the team's words.</param>
/// <param name="SendEmail">Send the candidate the rejection e-mail.</param>
/// <param name="EmailDelayDays">Days to wait before sending it; 0 sends it now.</param>
public sealed record RejectApplicationCommand(uint Version, string? ReasonCode, string? Note, bool SendEmail, int EmailDelayDays) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Applications.Manage;
}

/// <summary>Records that the candidate withdrew an active application, for a reason.</summary>
public sealed record WithdrawApplicationCommand(uint Version, string? ReasonCode, string? Note) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Applications.Manage;
}

/// <summary>Brings a rejected or withdrawn application back to the stage it left from.</summary>
public sealed record ReopenApplicationCommand(uint Version, string? Note) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Applications.Manage;
}

/// <summary>Changes the pipeline of an opening that is not closed; stages with applications stay (D50).</summary>
public sealed record UpdateOpeningStagesCommand(uint Version, IReadOnlyList<StageInput>? Stages) : ICommand, IRequirePermission, IStageListFields
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Openings.Manage;
}

/// <summary>Sends the rejection e-mails whose time came (the rejection e-mails job, D50); returns how many.</summary>
public sealed record SendDueRejectionEmailsCommand : ICommand<int>, IBulkCommand;

public static class ApplicationErrors
{
    public const string UnknownReasonCode = "recruitment.applications.errors.unknown_reason";
}

/// <summary>Who reaches the applications of an opening (D50).</summary>
public sealed class ApplicationAccess(IRecruitmentDbContext db, IAccessScopeProvider scopes, ICurrentUser currentUser)
{
    /// <summary>With the read permission over the opening's company; from self service, as a member of its hiring team.</summary>
    public async Task<bool> ReadsAsync(JobOpening opening, bool asTeam, CancellationToken cancellationToken) =>
        asTeam ? IsTeamMember(opening) : await RequisitionScope.CoversAsync(scopes, RecruitmentPermissions.Applications.Read, opening.CompanyId, cancellationToken);

    /// <summary>With the manage permission over the company; the hiring team only reads.</summary>
    public Task<bool> ManagesAsync(Guid companyId, CancellationToken cancellationToken) =>
        RequisitionScope.CoversAsync(scopes, RecruitmentPermissions.Applications.Manage, companyId, cancellationToken);

    /// <summary>The hiring manager or a recruiter of the opening; interviewers reach their candidates with R5.</summary>
    public bool IsTeamMember(JobOpening opening) =>
        currentUser.UserId is { } userId && (opening.HiringManagerUserId == userId || opening.Team.RecruiterUserIds.Contains(userId));

    /// <summary>The openings whose hiring team the caller is in, not drafts.</summary>
    public async Task<List<JobOpening>> TeamOpeningsAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return [];
        }

        // The team is a jsonb column: the recruiters are matched here, not in SQL
        var openings = await db.JobOpenings.AsNoTracking().Where(o => o.Status != OpeningStatus.Draft).ToListAsync(cancellationToken);
        return [.. openings.Where(IsTeamMember).OrderByDescending(o => o.PublishedAt)];
    }
}

/// <summary>Sends candidates the rejection e-mail (D47 <c>recruitment.application_rejected</c>, D50).</summary>
public sealed class RejectionEmails(
    IRecruitmentDbContext db,
    IOrganizationLookup organization,
    ILocalizationSource localization,
    IIntegrationEventPublisher publisher,
    TimeProvider timeProvider)
{
    public async Task SendAsync(IReadOnlyList<JobApplication> applications, CancellationToken cancellationToken)
    {
        if (applications.Count == 0)
        {
            return;
        }

        var candidateIds = applications.Select(a => a.CandidateId).Distinct().ToList();
        var candidates = await db.Candidates.AsNoTracking().Where(c => candidateIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
        var openingIds = applications.Select(a => a.OpeningId).Distinct().ToList();
        var openings = await db.JobOpenings.AsNoTracking().Where(o => openingIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Title, cancellationToken);
        var companies = (await organization.GetCompaniesAsync([.. applications.Select(a => a.CompanyId).Distinct()], cancellationToken))
            .ToDictionary(c => c.Id, c => c.ShortName);
        var defaultCulture = (await localization.GetAsync(cancellationToken)).DefaultCulture;
        var now = timeProvider.GetUtcNow();
        foreach (var application in applications)
        {
            var candidate = candidates[application.CandidateId];
            await publisher.PublishAsync(
                new ExternalEmailRequested(
                    RecruitmentNotificationTypes.ApplicationRejected,
                    candidate.Email,
                    candidate.FullName,
                    candidate.Culture ?? defaultCulture,
                    new Dictionary<string, string>
                    {
                        ["candidate"] = candidate.FullName,
                        ["position"] = openings.GetValueOrDefault(application.OpeningId) ?? string.Empty,
                        ["company"] = companies.GetValueOrDefault(application.CompanyId) ?? string.Empty,
                    })
                {
                    CompanyId = application.CompanyId,
                },
                cancellationToken);
            application.RejectionEmailSent(now);
        }
    }
}

/// <summary>Builds list rows of applications: candidate, opening and stage names.</summary>
public sealed class ApplicationRows(IRecruitmentDbContext db, ICurrentUser currentUser)
{
    /// <summary>Applications joined with their candidates, to filter and sort before the projection.</summary>
    public IQueryable<ApplicationRow> Query() =>
        from a in db.JobApplications.AsNoTracking()
        join c in db.Candidates.AsNoTracking() on a.CandidateId equals c.Id
        select new ApplicationRow
        {
            Id = a.Id,
            CandidateId = c.Id,
            FirstName = c.FirstName,
            LastName = c.LastName,
            Email = c.Email,
            Phone = c.Phone,
            OpeningId = a.OpeningId,
            CompanyId = a.CompanyId,
            StageId = a.StageId,
            Status = a.Status,
            KnockedOut = a.KnockedOut,
            IsInternal = a.IsInternal,
            Source = a.Source,
            AppliedAt = a.AppliedAt,
            StageEnteredAt = a.StageEnteredAt,
            HireRequestedAt = a.HireRequestedAt,
        };

    public async Task<List<ApplicationListItem>> ItemsAsync(IReadOnlyList<ApplicationRow> rows, CancellationToken cancellationToken)
    {
        var openingIds = rows.Select(r => r.OpeningId).Distinct().ToList();
        var openings = await db.JobOpenings.AsNoTracking().Where(o => openingIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);
        return [.. rows.Select(r => Item(r, openings[r.OpeningId]))];
    }

    public ApplicationListItem Item(ApplicationRow row, JobOpening opening)
    {
        var stage = opening.Stages.FirstOrDefault(s => s.Id == row.StageId);
        return new ApplicationListItem(
            row.Id.Value,
            row.CandidateId.Value,
            $"{row.FirstName} {row.LastName}",
            row.Email,
            row.Phone,
            opening.Id.Value,
            opening.Title,
            row.CompanyId,
            row.StageId,
            stage?.Name.Get(currentUser.Culture, "tr-TR"),
            stage?.Type,
            row.Status,
            row.KnockedOut,
            row.IsInternal,
            row.Source,
            row.AppliedAt,
            row.StageEnteredAt,
            row.Status == ApplicationStatus.Active && stage?.SlaDays is { } days ? row.StageEnteredAt.AddDays(days) : null,
            row.HireRequestedAt);
    }
}

/// <summary>An application with its candidate; EF filters and sorts on it (it cannot on a record built by its constructor).</summary>
public sealed class ApplicationRow
{
    public JobApplicationId Id { get; init; }

    public CandidateId CandidateId { get; init; }

    public string FirstName { get; init; } = null!;

    public string LastName { get; init; } = null!;

    public string Email { get; init; } = null!;

    public string? Phone { get; init; }

    public JobOpeningId OpeningId { get; init; }

    public Guid CompanyId { get; init; }

    public Guid StageId { get; init; }

    public ApplicationStatus Status { get; init; }

    public bool KnockedOut { get; init; }

    public bool IsInternal { get; init; }

    public string Source { get; init; } = null!;

    public DateTimeOffset AppliedAt { get; init; }

    public DateTimeOffset StageEnteredAt { get; init; }

    public DateTimeOffset? HireRequestedAt { get; init; }
}

internal sealed class GetApplicationsQueryValidator : AbstractValidator<GetApplicationsQuery>
{
    public GetApplicationsQueryValidator()
    {
        RuleFor(q => q.Paging.Sort).Must(GetApplicationsQuery.Sorts.IsValid).WithErrorCode(CommonErrors.InvalidSort.Code).OverridePropertyName("sort");
        RuleForEach(q => q.Statuses).IsInEnum().OverridePropertyName("status");
        RuleFor(q => q.OpeningId).NotNull().When(q => q.AsTeam);
    }
}

internal sealed class MoveApplicationCommandValidator : AbstractValidator<MoveApplicationCommand>
{
    public MoveApplicationCommandValidator()
    {
        RuleFor(c => c.StageId).NotEmpty();
        RuleFor(c => c.Note).MaximumLength(JobApplication.NoteMaxLength);
    }
}

internal sealed class RejectApplicationCommandValidator : AbstractValidator<RejectApplicationCommand>
{
    public RejectApplicationCommandValidator()
    {
        RuleFor(c => c.ReasonCode).NotEmpty().MaximumLength(RecruitmentLookup.CodeMaxLength);
        RuleFor(c => c.Note).MaximumLength(JobApplication.NoteMaxLength);
        RuleFor(c => c.EmailDelayDays).InclusiveBetween(0, JobApplication.MaxEmailDelayDays);
    }
}

internal sealed class WithdrawApplicationCommandValidator : AbstractValidator<WithdrawApplicationCommand>
{
    public WithdrawApplicationCommandValidator()
    {
        RuleFor(c => c.ReasonCode).NotEmpty().MaximumLength(RecruitmentLookup.CodeMaxLength);
        RuleFor(c => c.Note).MaximumLength(JobApplication.NoteMaxLength);
    }
}

internal sealed class ReopenApplicationCommandValidator : AbstractValidator<ReopenApplicationCommand>
{
    public ReopenApplicationCommandValidator() => RuleFor(c => c.Note).MaximumLength(JobApplication.NoteMaxLength);
}

internal sealed class UpdateOpeningStagesCommandValidator : AbstractValidator<UpdateOpeningStagesCommand>
{
    public UpdateOpeningStagesCommandValidator(ILocalizationSource localization) => StageTemplateRules.AddStageRules(this, localization);
}

internal sealed class GetOpeningPipelineQueryHandler(IRecruitmentDbContext db, ApplicationAccess access, IOrganizationLookup organization, ICurrentUser currentUser)
    : IQueryHandler<GetOpeningPipelineQuery, OpeningPipeline>
{
    public async Task<Result<OpeningPipeline>> Handle(GetOpeningPipelineQuery request, CancellationToken cancellationToken)
    {
        var opening = await db.JobOpenings.AsNoTracking().SingleOrDefaultAsync(o => o.Id == JobOpeningId.From(request.Id), cancellationToken);
        if (opening is null || !await access.ReadsAsync(opening, request.AsTeam, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        var counts = await db.JobApplications.AsNoTracking()
            .Where(a => a.OpeningId == opening.Id)
            .GroupBy(a => a.StageId)
            .Select(g => new { StageId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.StageId, g => g.Count, cancellationToken);
        var company = (await organization.GetCompaniesAsync([opening.CompanyId], cancellationToken)).SingleOrDefault();
        var manages = !request.AsTeam && await access.ManagesAsync(opening.CompanyId, cancellationToken);
        var reasons = manages
            ? (await db.Lookups.AsNoTracking()
                    .Where(l => l.IsActive && (l.Kind == LookupKind.RejectionReason || l.Kind == LookupKind.WithdrawalReason))
                    .OrderBy(l => l.SortOrder)
                    .ToListAsync(cancellationToken))
                .ToLookup(l => l.Kind, l => new ReasonOption(l.Code, l.Name.Get(currentUser.Culture, "tr-TR")))
            : Enumerable.Empty<ReasonOption>().ToLookup(_ => LookupKind.RejectionReason);
        return new OpeningPipeline(
            opening.Id.Value,
            opening.Title,
            opening.CompanyId,
            company?.ShortName,
            opening.Status,
            [.. opening.Stages.Select(s => new PipelineStage(s.Id, s.Name.Get(currentUser.Culture, "tr-TR"), s.Type, s.SlaDays, counts.GetValueOrDefault(s.Id)))],
            manages,
            [.. reasons[LookupKind.RejectionReason]],
            [.. reasons[LookupKind.WithdrawalReason]]);
    }
}

internal sealed class GetApplicationsQueryHandler(IRecruitmentDbContext db, IAccessScopeProvider scopes, ApplicationAccess access, ApplicationRows rows)
    : IQueryHandler<GetApplicationsQuery, PagedResult<ApplicationListItem>>
{
    public async Task<Result<PagedResult<ApplicationListItem>>> Handle(GetApplicationsQuery request, CancellationToken cancellationToken)
    {
        var query = rows.Query();
        if (request.AsTeam)
        {
            var opening = await db.JobOpenings.AsNoTracking().SingleOrDefaultAsync(o => o.Id == JobOpeningId.From(request.OpeningId!.Value), cancellationToken);
            if (opening is null || !access.IsTeamMember(opening))
            {
                return CommonErrors.NotFound;
            }
        }
        else if (await RecruitmentSettingRules.CompaniesAsync(scopes, request.Permission, cancellationToken) is { } companies)
        {
            var ids = companies.ToList();
            query = query.Where(r => ids.Contains(r.CompanyId));
        }

        if (request.OpeningId is { } openingId)
        {
            query = query.Where(r => r.OpeningId == JobOpeningId.From(openingId));
        }

        if (request.StageId is { } stageId)
        {
            query = query.Where(r => r.StageId == stageId);
        }

        if (request.Statuses is { Count: > 0 } statuses)
        {
            query = query.Where(r => statuses.Contains(r.Status));
        }

        if (request.KnockedOut is { } knockedOut)
        {
            query = query.Where(r => r.KnockedOut == knockedOut);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = TextSearch.ContainsPattern(request.Search);
            query = query.Where(r => TextSearch.Matches(r.FirstName + " " + r.LastName, pattern) || TextSearch.Matches(r.Email, pattern));
        }

        var page = await GetApplicationsQuery.Sorts.Apply(query, request.Paging.Sort, GetApplicationsQuery.DefaultSort)
            .ToPagedResultAsync(request.Paging, cancellationToken);
        return new PagedResult<ApplicationListItem>(await rows.ItemsAsync(page.Items, cancellationToken), page.Page, page.PageSize, page.TotalCount);
    }
}

internal sealed class GetApplicationQueryHandler(
    IRecruitmentDbContext db,
    ApplicationAccess access,
    ApplicationRows rows,
    IUserDirectory users,
    IAccessScopeProvider scopes,
    ApplicationCvAnalyzer analyzer,
    InterviewRows interviewRows,
    InterviewAccess interviewAccess,
    OfferRows offerRows,
    OfferAccess offerAccess,
    ICurrentUser currentUser) : IQueryHandler<GetApplicationQuery, ApplicationDetails>
{
    public async Task<Result<ApplicationDetails>> Handle(GetApplicationQuery request, CancellationToken cancellationToken)
    {
        var id = JobApplicationId.From(request.Id);
        var application = await db.JobApplications.AsNoTracking().Include(a => a.History).SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        var opening = application is null ? null : await db.JobOpenings.AsNoTracking().SingleAsync(o => o.Id == application.OpeningId, cancellationToken);
        if (application is null || opening is null || !await access.ReadsAsync(opening, request.AsTeam, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        var version = await db.JobApplications.AsNoTracking().Where(a => a.Id == id).Select(a => EF.Property<uint>(a, ConcurrencyVersion.PropertyName)).SingleAsync(cancellationToken);
        var row = await rows.Query().SingleAsync(r => r.Id == id, cancellationToken);
        var culture = currentUser.Culture;
        var history = application.History.OrderBy(h => h.Sequence).ToList();
        var reasonCodes = history.Select(h => h.ReasonCode).Append(application.ReasonCode).OfType<string>().Distinct().ToList();
        var reasons = (await db.Lookups.AsNoTracking()
                .Where(l => (l.Kind == LookupKind.RejectionReason || l.Kind == LookupKind.WithdrawalReason) && reasonCodes.Contains(l.Code))
                .ToListAsync(cancellationToken))
            .ToDictionary(l => (l.Kind, l.Code), l => l.Name.Get(culture, "tr-TR"));
        string? Reason(ApplicationStatus status, string? code) =>
            code is null ? null : reasons.GetValueOrDefault((status == ApplicationStatus.Withdrawn ? LookupKind.WithdrawalReason : LookupKind.RejectionReason, code));
        var cvAnalysis = await db.CvAnalyses.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        var names = (await users.GetAsync(
                [.. history.Where(h => h.UserId is not null).Select(h => h.UserId!.Value).Append(cvAnalysis?.RequestedByUserId ?? Guid.Empty).Where(u => u != Guid.Empty).Distinct()],
                cancellationToken))
            .ToDictionary(u => u.Id, u => u.DisplayName);
        string? StageName(Guid? stageId) => opening.Stages.FirstOrDefault(s => s.Id == stageId)?.Name.Get(culture, "tr-TR");
        var interviews = await db.Interviews.AsNoTracking().Where(i => i.ApplicationId == id).OrderBy(i => i.StartsAt).ToListAsync(cancellationToken);
        var forms = await db.InterviewFeedbacks.AsNoTracking().Where(f => f.ApplicationId == id).ToListAsync(cancellationToken);
        var offers = !request.AsTeam && await offerAccess.ReadsAsync(application.CompanyId, cancellationToken)
            ? await db.JobOffers.AsNoTracking().Where(o => o.ApplicationId == id).OrderBy(o => o.CreatedAt).ToListAsync(cancellationToken)
            : [];
        var active = !request.AsTeam && application.Status == ApplicationStatus.Active;
        var cv = application.CvFileId is { } fileId
            ? await db.StoredFiles.AsNoTracking().Where(f => f.Id == fileId).Select(f => new ApplicationCvItem(f.FileName, f.ContentType, f.Length)).SingleOrDefaultAsync(cancellationToken)
            : null;
        return new ApplicationDetails(
            rows.Item(row, opening),
            application.CoverLetter,
            [.. application.Answers.Select(a => Answer(opening, a))],
            cv,
            application.Consent,
            application.ReasonCode,
            Reason(application.Status, application.ReasonCode),
            application.RejectionEmailDueAt,
            application.RejectionEmailSentAt,
            [
                .. history.Select(h => new ApplicationStageChangeItem(
                    h.FromStageId,
                    StageName(h.FromStageId),
                    h.ToStageId,
                    StageName(h.ToStageId),
                    h.Status,
                    h.ReasonCode,
                    Reason(h.Status, h.ReasonCode),
                    h.Note,
                    h.UserId,
                    h.UserId is { } userId ? names.GetValueOrDefault(userId) : null,
                    h.At)),
            ],
            !request.AsTeam && await access.ManagesAsync(application.CompanyId, cancellationToken),
            version,
            ApplicationCvAnalyzer.Item(cvAnalysis, cvAnalysis?.RequestedByUserId is { } requester ? names.GetValueOrDefault(requester) : null),
            !request.AsTeam
                && application.CvFileId is not null
                && analyzer.IsEnabled
                && (await scopes.GetCurrentUserScopesAsync(RecruitmentPermissions.Cv.Analyze, cancellationToken)).Count > 0,
            await interviewRows.ItemsAsync(interviews, cancellationToken),
            await interviewRows.FeedbackAsync(forms, interviews.ToDictionary(i => i.Id), cancellationToken),
            InterviewRows.Score(forms),
            active && await interviewAccess.ManagesAsync(application.CompanyId, cancellationToken),
            await offerRows.ItemsAsync(offers, cancellationToken),
            active && !offers.Any(o => o.IsOpen || o.Status == OfferStatus.Accepted) && await offerAccess.ManagesAsync(application.CompanyId, cancellationToken),
            active && application.HireRequestedAt is null && offers.Any(o => o.Status == OfferStatus.Accepted) && await access.ManagesAsync(application.CompanyId, cancellationToken));
    }

    private static ApplicationAnswerItem Answer(JobOpening opening, ApplicationAnswer answer)
    {
        var question = opening.Form.Questions.FirstOrDefault(q => q.Id == answer.QuestionId);
        var missed = question is { IsKnockout: true } && answer.Values.Any(v => !question.AcceptedAnswers!.Contains(v, StringComparer.Ordinal));
        return new ApplicationAnswerItem(answer.QuestionId, question?.Text, question?.Type, answer.Values, missed);
    }
}

internal sealed class GetApplicationCvQueryHandler(IRecruitmentDbContext db, ApplicationAccess access, IFileStore files)
    : IQueryHandler<GetApplicationCvQuery, RecruitmentFileDownload>
{
    public async Task<Result<RecruitmentFileDownload>> Handle(GetApplicationCvQuery request, CancellationToken cancellationToken)
    {
        var application = await db.JobApplications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == JobApplicationId.From(request.Id), cancellationToken);
        var opening = application is null ? null : await db.JobOpenings.AsNoTracking().SingleAsync(o => o.Id == application.OpeningId, cancellationToken);
        if (application is null || opening is null || !await access.ReadsAsync(opening, request.AsTeam, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        var file = application.CvFileId is { } fileId ? await db.StoredFiles.AsNoTracking().SingleOrDefaultAsync(f => f.Id == fileId, cancellationToken) : null;
        if (file is null)
        {
            return CommonErrors.NotFound;
        }

        return new RecruitmentFileDownload(file.FileName, file.ContentType, file.Length, await files.OpenReadAsync(file, cancellationToken));
    }
}

internal sealed class GetHiringOpeningsQueryHandler(ApplicationAccess access, OpeningRows rows) : IQueryHandler<GetHiringOpeningsQuery, IReadOnlyList<OpeningListItem>>
{
    public async Task<Result<IReadOnlyList<OpeningListItem>>> Handle(GetHiringOpeningsQuery request, CancellationToken cancellationToken) =>
        await rows.RowsAsync(await access.TeamOpeningsAsync(cancellationToken), cancellationToken);
}

/// <summary>Loads an application to change, with its history (the next entry is numbered after it) and its opening.</summary>
internal static class ApplicationChanges
{
    public static async Task<Result<(JobApplication Application, JobOpening Opening)>> LoadAsync(
        IRecruitmentDbContext db,
        ApplicationAccess access,
        Guid id,
        uint version,
        CancellationToken cancellationToken)
    {
        var application = await db.JobApplications.Include(a => a.History).SingleOrDefaultAsync(a => a.Id == JobApplicationId.From(id), cancellationToken);
        if (application is null || !await access.ManagesAsync(application.CompanyId, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        db.Entry(application).ExpectVersion(version);
        var opening = await db.JobOpenings.AsNoTracking().SingleAsync(o => o.Id == application.OpeningId, cancellationToken);
        return (application, opening);
    }

    /// <summary>The code of an active reason of the list, as stored (upper case); null when there is none.</summary>
    public static async Task<string?> ReasonAsync(IRecruitmentDbContext db, LookupKind kind, string code, CancellationToken cancellationToken)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return await db.Lookups.AnyAsync(l => l.Kind == kind && l.Code == normalized && l.IsActive, cancellationToken) ? normalized : null;
    }

    public static Error UnknownReason => Error.Validation([new FieldError("reasonCode", ApplicationErrors.UnknownReasonCode)]);
}

internal sealed class MoveApplicationCommandHandler(IRecruitmentDbContext db, ApplicationAccess access, ICurrentUser currentUser, TimeProvider timeProvider)
    : ICommandHandler<MoveApplicationCommand>
{
    public async Task<Result> Handle(MoveApplicationCommand request, CancellationToken cancellationToken)
    {
        var loaded = await ApplicationChanges.LoadAsync(db, access, request.Id, request.Version, cancellationToken);
        return loaded.IsFailure
            ? loaded.Error
            : loaded.Value.Application.MoveTo(loaded.Value.Opening, request.StageId, request.Note, currentUser.UserId, timeProvider.GetUtcNow());
    }
}

internal sealed class RejectApplicationCommandHandler(
    IRecruitmentDbContext db,
    ApplicationAccess access,
    RejectionEmails emails,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<RejectApplicationCommand>
{
    public async Task<Result> Handle(RejectApplicationCommand request, CancellationToken cancellationToken)
    {
        var loaded = await ApplicationChanges.LoadAsync(db, access, request.Id, request.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        if (await ApplicationChanges.ReasonAsync(db, LookupKind.RejectionReason, request.ReasonCode!, cancellationToken) is not { } reason)
        {
            return ApplicationChanges.UnknownReason;
        }

        var (application, opening) = loaded.Value;
        var now = timeProvider.GetUtcNow();
        var rejected = application.Reject(opening, reason, request.Note, request.SendEmail ? now.AddDays(request.EmailDelayDays) : null, currentUser.UserId, now);
        if (rejected.IsSuccess && request.SendEmail && request.EmailDelayDays == 0)
        {
            await emails.SendAsync([application], cancellationToken);
        }

        return rejected;
    }
}

internal sealed class WithdrawApplicationCommandHandler(IRecruitmentDbContext db, ApplicationAccess access, ICurrentUser currentUser, TimeProvider timeProvider)
    : ICommandHandler<WithdrawApplicationCommand>
{
    public async Task<Result> Handle(WithdrawApplicationCommand request, CancellationToken cancellationToken)
    {
        var loaded = await ApplicationChanges.LoadAsync(db, access, request.Id, request.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        return await ApplicationChanges.ReasonAsync(db, LookupKind.WithdrawalReason, request.ReasonCode!, cancellationToken) is { } reason
            ? loaded.Value.Application.Withdraw(loaded.Value.Opening, reason, request.Note, currentUser.UserId, timeProvider.GetUtcNow())
            : ApplicationChanges.UnknownReason;
    }
}

internal sealed class ReopenApplicationCommandHandler(IRecruitmentDbContext db, ApplicationAccess access, ICurrentUser currentUser, TimeProvider timeProvider)
    : ICommandHandler<ReopenApplicationCommand>
{
    public async Task<Result> Handle(ReopenApplicationCommand request, CancellationToken cancellationToken)
    {
        var loaded = await ApplicationChanges.LoadAsync(db, access, request.Id, request.Version, cancellationToken);
        return loaded.IsFailure
            ? loaded.Error
            : loaded.Value.Application.Reopen(loaded.Value.Opening, request.Note, currentUser.UserId, timeProvider.GetUtcNow());
    }
}

internal sealed class UpdateOpeningStagesCommandHandler(IRecruitmentDbContext db, IAccessScopeProvider scopes) : ICommandHandler<UpdateOpeningStagesCommand>
{
    public async Task<Result> Handle(UpdateOpeningStagesCommand request, CancellationToken cancellationToken)
    {
        var opening = await db.JobOpenings.SingleOrDefaultAsync(o => o.Id == JobOpeningId.From(request.Id), cancellationToken);
        if (opening is null || !await RequisitionScope.CoversAsync(scopes, request.Permission, opening.CompanyId, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        db.Entry(opening).ExpectVersion(request.Version);
        var used = await db.JobApplications.AsNoTracking()
            .Where(a => a.OpeningId == opening.Id)
            .Select(a => new { a.StageId, a.PreviousStageId })
            .ToListAsync(cancellationToken);
        var inUse = used.Select(u => u.StageId).Concat(used.Where(u => u.PreviousStageId is not null).Select(u => u.PreviousStageId!.Value)).ToHashSet();
        return opening.ChangeStages(StageTemplateRules.ToStages(request.Stages!), inUse);
    }
}

internal sealed class SendDueRejectionEmailsCommandHandler(IRecruitmentDbContext db, RejectionEmails emails, TimeProvider timeProvider)
    : ICommandHandler<SendDueRejectionEmailsCommand, int>
{
    private const int BatchSize = 500;

    public async Task<Result<int>> Handle(SendDueRejectionEmailsCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var due = await db.JobApplications
            .Where(a => a.RejectionEmailDueAt != null && a.RejectionEmailDueAt <= now && a.Status == ApplicationStatus.Rejected)
            .OrderBy(a => a.RejectionEmailDueAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        await emails.SendAsync(due, cancellationToken);
        return due.Count;
    }
}
