using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Documents;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.Modules.Platform.Contracts.Users;
using Merp.Modules.Recruitment.Application.Requisitions;
using Merp.Modules.Recruitment.Application.Settings;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Recruitment.Application.Openings;

/// <summary>A question as the form sends it; a new question has no id.</summary>
/// <param name="Id">The question's id; empty for a new one.</param>
/// <param name="Text">The question.</param>
/// <param name="Type">How it is answered.</param>
/// <param name="Required">An answer is needed.</param>
/// <param name="Options">The choices of a choice question.</param>
/// <param name="AcceptedAnswers">For a knockout question, the acceptable answers; empty for an ordinary one.</param>
public sealed record OpeningQuestionInput(Guid? Id, string? Text, QuestionType Type, bool Required, IReadOnlyList<string>? Options, IReadOnlyList<string>? AcceptedAnswers);

/// <summary>What the opening form gives (D49).</summary>
public interface IOpeningFields
{
    Guid CompanyId { get; }

    /// <summary>An approved requisition of the company; null for none.</summary>
    Guid? RequisitionId { get; }

    string? Title { get; }

    /// <summary>HTML from the editor; sanitized before it is saved.</summary>
    string? Description { get; }

    /// <summary>HTML from the editor; sanitized before it is saved.</summary>
    string? Qualifications { get; }

    Guid? BranchId { get; }

    WorkModel WorkModel { get; }

    RequisitionEmploymentType EmploymentType { get; }

    int Headcount { get; }

    OpeningVisibility Visibility { get; }

    DateOnly? PublishFrom { get; }

    DateOnly? PublishUntil { get; }

    /// <summary>The template whose stages the opening copies; the tenant's default when null.</summary>
    Guid? StageTemplateId { get; }

    Guid HiringManagerUserId { get; }

    IReadOnlyList<Guid>? RecruiterUserIds { get; }

    IReadOnlyList<Guid>? InterviewerUserIds { get; }

    bool RequirePhone { get; }

    bool RequireCv { get; }

    bool AskCoverLetter { get; }

    IReadOnlyList<OpeningQuestionInput>? Questions { get; }
}

/// <summary>An opening in a list.</summary>
public sealed record OpeningListItem(
    Guid Id,
    Guid CompanyId,
    string? CompanyName,
    string Title,
    string? BranchName,
    WorkModel WorkModel,
    RequisitionEmploymentType EmploymentType,
    int Headcount,
    OpeningVisibility Visibility,
    OpeningStatus Status,
    DateOnly? PublishFrom,
    DateOnly? PublishUntil,
    Guid? RequisitionId,
    string? RequisitionNumber,
    string? HiringManagerName,
    DateTimeOffset CreatedAt);

/// <summary>A stage of an opening's pipeline, named in the user's language.</summary>
public sealed record OpeningStageItem(Guid Id, string Name, StageType Type, int? SlaDays);

/// <summary>A member of the hiring team.</summary>
public sealed record OpeningMember(Guid UserId, string? DisplayName);

/// <summary>An opening with its texts, pipeline, team, form and the caller's actions (D49).</summary>
public sealed record OpeningDetails(
    OpeningListItem Opening,
    string? Description,
    string? Qualifications,
    Guid? BranchId,
    Guid StageTemplateId,
    IReadOnlyList<OpeningStageItem> Stages,
    OpeningMember HiringManager,
    IReadOnlyList<OpeningMember> Recruiters,
    IReadOnlyList<OpeningMember> Interviewers,
    ApplicationForm Form,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ClosedAt,
    string? CloseReason,
    bool CanEdit,
    bool CanChangeStages,
    bool CanPublish,
    bool CanHold,
    bool CanClose,
    uint Version);

/// <summary>The openings of the companies in the caller's scope, newest first unless sorted.</summary>
public sealed record GetOpeningsQuery(
    PageRequest Paging,
    Guid? CompanyId = null,
    IReadOnlyList<OpeningStatus>? Statuses = null,
    OpeningVisibility? Visibility = null,
    Guid? RequisitionId = null,
    string? Search = null) : IQuery<PagedResult<OpeningListItem>>, IRequirePermission
{
    public const string DefaultSort = "-createdAt";

    internal static readonly SortMap<JobOpening> Sorts = SortMap<JobOpening>.By(o => o.Id)
        .Add("createdAt", o => o.CreatedAt)
        .Add("title", o => o.Title)
        .Add("publishFrom", o => o.PublishFrom)
        .Add("publishUntil", o => o.PublishUntil);

    public string Permission => RecruitmentPermissions.Openings.Read;
}

/// <summary>An opening of a company in scope.</summary>
public sealed record GetOpeningQuery(Guid Id) : IQuery<OpeningDetails>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Openings.Read;
}

/// <summary>Opens an opening as a draft; returns its id.</summary>
public sealed record CreateOpeningCommand(
    Guid CompanyId,
    Guid? RequisitionId,
    string? Title,
    string? Description,
    string? Qualifications,
    Guid? BranchId,
    WorkModel WorkModel,
    RequisitionEmploymentType EmploymentType,
    int Headcount,
    OpeningVisibility Visibility,
    DateOnly? PublishFrom,
    DateOnly? PublishUntil,
    Guid? StageTemplateId,
    Guid HiringManagerUserId,
    IReadOnlyList<Guid>? RecruiterUserIds,
    IReadOnlyList<Guid>? InterviewerUserIds,
    bool RequirePhone,
    bool RequireCv,
    bool AskCoverLetter,
    IReadOnlyList<OpeningQuestionInput>? Questions) : ICommand<Guid>, IRequirePermission, IOpeningFields
{
    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Openings.Manage;
}

/// <summary>Corrects an opening that is not closed; its stages change only while it is a draft.</summary>
public sealed record UpdateOpeningCommand(
    uint Version,
    Guid CompanyId,
    Guid? RequisitionId,
    string? Title,
    string? Description,
    string? Qualifications,
    Guid? BranchId,
    WorkModel WorkModel,
    RequisitionEmploymentType EmploymentType,
    int Headcount,
    OpeningVisibility Visibility,
    DateOnly? PublishFrom,
    DateOnly? PublishUntil,
    Guid? StageTemplateId,
    Guid HiringManagerUserId,
    IReadOnlyList<Guid>? RecruiterUserIds,
    IReadOnlyList<Guid>? InterviewerUserIds,
    bool RequirePhone,
    bool RequireCv,
    bool AskCoverLetter,
    IReadOnlyList<OpeningQuestionInput>? Questions) : ICommand, IRequirePermission, IOpeningFields
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Openings.Manage;
}

/// <summary>Publishes a draft or an opening on hold.</summary>
public sealed record PublishOpeningCommand(Guid Id) : ICommand, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Openings.Manage;
}

/// <summary>Pauses a published opening.</summary>
public sealed record HoldOpeningCommand(Guid Id) : ICommand, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Openings.Manage;
}

/// <summary>Closes an opening.</summary>
public sealed record CloseOpeningCommand(string? Reason) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => RecruitmentPermissions.Openings.Manage;
}

/// <summary>A stage template an opening may copy.</summary>
public sealed record OpeningTemplateOption(Guid Id, string Name, bool IsDefault, IReadOnlyList<OpeningStageItem> Stages);

/// <summary>The active stage templates, the default first, for the opening form.</summary>
public sealed record GetOpeningTemplatesQuery : IQuery<IReadOnlyList<OpeningTemplateOption>>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Openings.Manage;
}

/// <summary>The active branches of a company, for the opening form.</summary>
public sealed record GetOpeningBranchesQuery(Guid CompanyId) : IQuery<IReadOnlyList<BranchSummary>>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Openings.Manage;
}

/// <summary>Active users by name, for the hiring team (at most 20).</summary>
public sealed record SearchOpeningUsersQuery(string? Search = null) : IQuery<IReadOnlyList<RequisitionUserOption>>, IRequirePermission
{
    public string Permission => RecruitmentPermissions.Openings.Manage;
}

public static class OpeningErrors
{
    public const string UnknownRequisitionCode = "recruitment.openings.errors.unknown_requisition";
    public const string RequisitionNotApprovedCode = "recruitment.openings.errors.requisition_not_approved";
    public const string OverRequisitionCode = "recruitment.openings.errors.over_requisition";
    public const string UnknownBranchCode = "recruitment.openings.errors.unknown_branch";
    public const string UnknownTemplateCode = "recruitment.openings.errors.unknown_template";
    public const string UnknownUserCode = "recruitment.openings.errors.unknown_user";
    public const string InvalidQuestionCode = "recruitment.openings.errors.invalid_question";
    public const string DateRangeCode = "recruitment.openings.errors.date_range";
    public const string CompanyInactiveCode = "recruitment.openings.errors.company_inactive";
}

/// <summary>Checks and builds what an opening says (D49).</summary>
public sealed class OpeningRules(IRecruitmentDbContext db, IOrganizationLookup organization, IUserDirectory users, IRichTextSanitizer sanitizer)
{
    /// <summary>
    /// The details of the form, checked: an approved requisition of the company with enough people left, a branch of
    /// the company, active users in the team, valid questions; the rich texts sanitized. Also returns the template to
    /// copy when one was chosen (or the default for a new opening).
    /// </summary>
    public async Task<Result<(OpeningTerms Terms, StageTemplate? Template)>> CheckAsync(IOpeningFields fields, bool isNew, CancellationToken cancellationToken)
    {
        JobRequisitionId? requisitionId = null;
        if (fields.RequisitionId is { } id)
        {
            var requisition = await db.JobRequisitions.AsNoTracking().SingleOrDefaultAsync(r => r.Id == JobRequisitionId.From(id), cancellationToken);
            if (requisition is null || requisition.CompanyId != fields.CompanyId)
            {
                return Field("requisitionId", OpeningErrors.UnknownRequisitionCode);
            }

            // A new opening needs an approved requisition; an existing one keeps its link whatever became of it
            if (isNew && requisition.Status != RequisitionStatus.Approved)
            {
                return Field("requisitionId", OpeningErrors.RequisitionNotApprovedCode);
            }

            var left = requisition.Headcount - requisition.FilledCount;
            if (fields.Headcount > left)
            {
                return Error.Validation([new FieldError("headcount", OpeningErrors.OverRequisitionCode, new Dictionary<string, object?> { ["available"] = Math.Max(left, 0) })]);
            }

            requisitionId = requisition.Id;
        }

        if (fields.BranchId is { } branchId)
        {
            var branch = (await organization.GetBranchesAsync([branchId], cancellationToken)).SingleOrDefault();
            if (branch is null || branch.CompanyId != fields.CompanyId)
            {
                return Field("branchId", OpeningErrors.UnknownBranchCode);
            }
        }

        StageTemplate? template = null;
        if (fields.StageTemplateId is { } templateId)
        {
            template = await db.StageTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.Id == StageTemplateId.From(templateId) && t.IsActive, cancellationToken);
            if (template is null)
            {
                return Field("stageTemplateId", OpeningErrors.UnknownTemplateCode);
            }
        }
        else if (isNew)
        {
            template = await db.StageTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.IsDefault, cancellationToken);
            if (template is null)
            {
                return Field("stageTemplateId", OpeningErrors.UnknownTemplateCode);
            }
        }

        var recruiters = fields.RecruiterUserIds ?? [];
        var interviewers = fields.InterviewerUserIds ?? [];
        var team = new[] { fields.HiringManagerUserId }.Concat(recruiters).Concat(interviewers).Distinct().ToList();
        var active = (await users.GetAsync(team, cancellationToken)).Where(u => u.IsActive).Select(u => u.Id).ToHashSet();
        if (!active.Contains(fields.HiringManagerUserId))
        {
            return Field("hiringManagerUserId", OpeningErrors.UnknownUserCode);
        }

        if (!recruiters.All(active.Contains))
        {
            return Field("recruiterUserIds", OpeningErrors.UnknownUserCode);
        }

        if (!interviewers.All(active.Contains))
        {
            return Field("interviewerUserIds", OpeningErrors.UnknownUserCode);
        }

        var questions = new List<OpeningQuestion>();
        foreach (var (input, index) in (fields.Questions ?? []).Select((q, i) => (q, i)))
        {
            var question = Question(input);
            if (question is null)
            {
                return Field($"questions[{index}]", OpeningErrors.InvalidQuestionCode);
            }

            questions.Add(question);
        }

        return (new OpeningTerms(
            fields.CompanyId,
            requisitionId,
            fields.Title!,
            sanitizer.Sanitize(fields.Description),
            sanitizer.Sanitize(fields.Qualifications),
            fields.BranchId,
            fields.WorkModel,
            fields.EmploymentType,
            fields.Headcount,
            fields.Visibility,
            fields.PublishFrom,
            fields.PublishUntil,
            new OpeningTeam(fields.HiringManagerUserId, [.. recruiters], [.. interviewers]),
            new ApplicationForm(fields.RequirePhone, fields.RequireCv, fields.AskCoverLetter, questions)), template);
    }

    /// <summary>
    /// A question as stored: choices have options; a knockout question is a yes/no or choice question whose accepted
    /// answers are among its answers. Null when it does not hold together.
    /// </summary>
    internal static OpeningQuestion? Question(OpeningQuestionInput input)
    {
        var options = (input.Options ?? []).Select(o => o?.Trim() ?? string.Empty).ToList();
        var accepted = (input.AcceptedAnswers ?? []).Select(a => a?.Trim() ?? string.Empty).Distinct(StringComparer.Ordinal).ToList();
        var type = input.Type;
        var choice = type is QuestionType.SingleChoice or QuestionType.MultiChoice;
        if (choice && (options.Count < 2 || options.Any(string.IsNullOrEmpty) || options.Distinct(StringComparer.Ordinal).Count() != options.Count))
        {
            return null;
        }

        string[] answers = type == QuestionType.YesNo ? [OpeningQuestion.Yes, OpeningQuestion.No] : [.. options];
        if (accepted.Count > 0 && (!(choice || type == QuestionType.YesNo) || !accepted.All(answers.Contains)))
        {
            return null;
        }

        return new OpeningQuestion(
            input.Id ?? Guid.Empty,
            input.Text!.Trim(),
            type,
            input.Required,
            choice ? options : [],
            accepted.Count > 0 ? accepted : null);
    }

    private static Error Field(string field, string code) => Error.Validation([new FieldError(field, code)]);

    public static void AddFieldRules<T>(AbstractValidator<T> validator)
        where T : IOpeningFields
    {
        validator.RuleFor(o => o.CompanyId).NotEmpty();
        validator.RuleFor(o => o.Title).NotEmpty().MaximumLength(JobOpening.TitleMaxLength);
        validator.RuleFor(o => o.Description).MaximumLength(JobOpening.RichTextMaxLength);
        validator.RuleFor(o => o.Qualifications).MaximumLength(JobOpening.RichTextMaxLength);
        validator.RuleFor(o => o.WorkModel).IsInEnum();
        validator.RuleFor(o => o.EmploymentType).IsInEnum();
        validator.RuleFor(o => o.Visibility).IsInEnum();
        validator.RuleFor(o => o.Headcount).InclusiveBetween(1, JobOpening.MaxHeadcount);
        validator.RuleFor(o => o.PublishUntil).GreaterThanOrEqualTo(o => o.PublishFrom).WithErrorCode(OpeningErrors.DateRangeCode)
            .When(o => o.PublishFrom is not null && o.PublishUntil is not null);
        validator.RuleFor(o => o.HiringManagerUserId).NotEmpty();
        validator.RuleFor(o => o.RecruiterUserIds).Must(ids => ids is null || ids.Count <= OpeningTeam.MaxMembers);
        validator.RuleFor(o => o.InterviewerUserIds).Must(ids => ids is null || ids.Count <= OpeningTeam.MaxMembers);
        validator.RuleFor(o => o.Questions).Must(q => q is null || q.Count <= ApplicationForm.MaxQuestions);
        validator.RuleForEach(o => o.Questions).ChildRules(question =>
        {
            question.RuleFor(q => q.Text).NotEmpty().MaximumLength(OpeningQuestion.TextMaxLength);
            question.RuleFor(q => q.Type).IsInEnum();
            question.RuleFor(q => q.Options).Must(o => o is null || o.Count <= OpeningQuestion.MaxOptions);
            question.RuleForEach(q => q.Options).MaximumLength(OpeningQuestion.OptionMaxLength);
        });
    }
}

/// <summary>Builds the list rows of openings: company, branch, requisition and hiring manager names.</summary>
public sealed class OpeningRows(IRecruitmentDbContext db, IOrganizationLookup organization, IUserDirectory users)
{
    public async Task<List<OpeningListItem>> RowsAsync(IReadOnlyList<JobOpening> openings, CancellationToken cancellationToken)
    {
        var companies = (await organization.GetCompaniesAsync([.. openings.Select(o => o.CompanyId).Distinct()], cancellationToken))
            .ToDictionary(c => c.Id, c => c.ShortName);
        var branches = (await organization.GetBranchesAsync([.. openings.Where(o => o.BranchId is not null).Select(o => o.BranchId!.Value).Distinct()], cancellationToken))
            .ToDictionary(b => b.Id, b => b.Name);
        var requisitionIds = openings.Where(o => o.RequisitionId is not null).Select(o => o.RequisitionId!.Value).Distinct().ToList();
        var requisitions = await db.JobRequisitions.AsNoTracking()
            .Where(r => requisitionIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Number, cancellationToken);
        var managers = (await users.GetAsync([.. openings.Select(o => o.HiringManagerUserId).Distinct()], cancellationToken))
            .ToDictionary(u => u.Id, u => u.DisplayName);
        return
        [
            .. openings.Select(o => new OpeningListItem(
                o.Id.Value,
                o.CompanyId,
                companies.GetValueOrDefault(o.CompanyId),
                o.Title,
                o.BranchId is { } branchId ? branches.GetValueOrDefault(branchId) : null,
                o.WorkModel,
                o.EmploymentType,
                o.Headcount,
                o.Visibility,
                o.Status,
                o.PublishFrom,
                o.PublishUntil,
                o.RequisitionId?.Value,
                o.RequisitionId is { } requisitionId ? requisitions.GetValueOrDefault(requisitionId) : null,
                managers.GetValueOrDefault(o.HiringManagerUserId),
                o.CreatedAt)),
        ];
    }
}

internal sealed class CreateOpeningCommandValidator : AbstractValidator<CreateOpeningCommand>
{
    public CreateOpeningCommandValidator() => OpeningRules.AddFieldRules(this);
}

internal sealed class UpdateOpeningCommandValidator : AbstractValidator<UpdateOpeningCommand>
{
    public UpdateOpeningCommandValidator() => OpeningRules.AddFieldRules(this);
}

internal sealed class CloseOpeningCommandValidator : AbstractValidator<CloseOpeningCommand>
{
    public CloseOpeningCommandValidator() => RuleFor(c => c.Reason).MaximumLength(JobOpening.ReasonMaxLength);
}

internal sealed class GetOpeningsQueryValidator : AbstractValidator<GetOpeningsQuery>
{
    public GetOpeningsQueryValidator()
    {
        RuleFor(q => q.Paging.Sort).Must(GetOpeningsQuery.Sorts.IsValid).WithErrorCode(CommonErrors.InvalidSort.Code).OverridePropertyName("sort");
        RuleForEach(q => q.Statuses).IsInEnum().OverridePropertyName("status");
        RuleFor(q => q.Visibility).IsInEnum();
    }
}

internal sealed class CreateOpeningCommandHandler(IRecruitmentDbContext db, IAccessScopeProvider scopes, IOrganizationLookup organization, OpeningRules rules)
    : ICommandHandler<CreateOpeningCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateOpeningCommand request, CancellationToken cancellationToken)
    {
        if (!await RequisitionScope.CoversAsync(scopes, request.Permission, request.CompanyId, cancellationToken))
        {
            return CommonErrors.Forbidden;
        }

        if ((await organization.GetCompaniesAsync([request.CompanyId], cancellationToken)).SingleOrDefault() is not { IsActive: true })
        {
            return Error.Validation([new FieldError("companyId", OpeningErrors.CompanyInactiveCode)]);
        }

        var checkedTerms = await rules.CheckAsync(request, isNew: true, cancellationToken);
        if (checkedTerms.IsFailure)
        {
            return checkedTerms.Error;
        }

        var opening = JobOpening.Draft(checkedTerms.Value.Terms, checkedTerms.Value.Template!);
        db.JobOpenings.Add(opening);
        return opening.Id.Value;
    }
}

internal sealed class UpdateOpeningCommandHandler(IRecruitmentDbContext db, IAccessScopeProvider scopes, OpeningRules rules) : ICommandHandler<UpdateOpeningCommand>
{
    public async Task<Result> Handle(UpdateOpeningCommand request, CancellationToken cancellationToken)
    {
        var opening = await db.JobOpenings.SingleOrDefaultAsync(o => o.Id == JobOpeningId.From(request.Id), cancellationToken);
        if (opening is null || !await RequisitionScope.CoversAsync(scopes, request.Permission, opening.CompanyId, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        db.Entry(opening).ExpectVersion(request.Version);
        var checkedTerms = await rules.CheckAsync(request, isNew: false, cancellationToken);
        return checkedTerms.IsFailure ? checkedTerms.Error : opening.Update(checkedTerms.Value.Terms, checkedTerms.Value.Template);
    }
}

internal sealed class PublishOpeningCommandHandler(
    IRecruitmentDbContext db,
    IAccessScopeProvider scopes,
    IOrganizationLookup organization,
    TimeProvider timeProvider) : ICommandHandler<PublishOpeningCommand>
{
    public async Task<Result> Handle(PublishOpeningCommand request, CancellationToken cancellationToken)
    {
        var opening = await db.JobOpenings.SingleOrDefaultAsync(o => o.Id == JobOpeningId.From(request.Id), cancellationToken);
        if (opening is null || !await RequisitionScope.CoversAsync(scopes, request.Permission, opening.CompanyId, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        // The requisition may have been cancelled or filled since the opening was drafted
        if (opening.RequisitionId is { } requisitionId
            && !await db.JobRequisitions.AnyAsync(r => r.Id == requisitionId && r.Status == RequisitionStatus.Approved, cancellationToken))
        {
            return Error.Validation([new FieldError("requisitionId", OpeningErrors.RequisitionNotApprovedCode)]);
        }

        var company = (await organization.GetCompaniesAsync([opening.CompanyId], cancellationToken)).SingleOrDefault();
        if (company is not { IsActive: true })
        {
            return Error.Validation([new FieldError("companyId", OpeningErrors.CompanyInactiveCode)]);
        }

        return opening.Publish(RecruitmentClock.Today(timeProvider, company.TimeZoneId), timeProvider.GetUtcNow());
    }
}

internal sealed class HoldOpeningCommandHandler(IRecruitmentDbContext db, IAccessScopeProvider scopes) : ICommandHandler<HoldOpeningCommand>
{
    public async Task<Result> Handle(HoldOpeningCommand request, CancellationToken cancellationToken)
    {
        var opening = await db.JobOpenings.SingleOrDefaultAsync(o => o.Id == JobOpeningId.From(request.Id), cancellationToken);
        return opening is null || !await RequisitionScope.CoversAsync(scopes, request.Permission, opening.CompanyId, cancellationToken)
            ? CommonErrors.NotFound
            : opening.Hold();
    }
}

internal sealed class CloseOpeningCommandHandler(IRecruitmentDbContext db, IAccessScopeProvider scopes, TimeProvider timeProvider) : ICommandHandler<CloseOpeningCommand>
{
    public async Task<Result> Handle(CloseOpeningCommand request, CancellationToken cancellationToken)
    {
        var opening = await db.JobOpenings.SingleOrDefaultAsync(o => o.Id == JobOpeningId.From(request.Id), cancellationToken);
        return opening is null || !await RequisitionScope.CoversAsync(scopes, request.Permission, opening.CompanyId, cancellationToken)
            ? CommonErrors.NotFound
            : opening.Close(request.Reason, timeProvider.GetUtcNow());
    }
}

internal sealed class GetOpeningsQueryHandler(IRecruitmentDbContext db, IAccessScopeProvider scopes, OpeningRows rows)
    : IQueryHandler<GetOpeningsQuery, PagedResult<OpeningListItem>>
{
    public async Task<Result<PagedResult<OpeningListItem>>> Handle(GetOpeningsQuery request, CancellationToken cancellationToken)
    {
        var companies = await RecruitmentSettingRules.CompaniesAsync(scopes, request.Permission, cancellationToken);
        var openings = db.JobOpenings.AsNoTracking();
        if (companies is not null)
        {
            var ids = companies.ToList();
            openings = openings.Where(o => ids.Contains(o.CompanyId));
        }

        if (request.CompanyId is { } companyId)
        {
            openings = openings.Where(o => o.CompanyId == companyId);
        }

        if (request.Statuses is { Count: > 0 } statuses)
        {
            openings = openings.Where(o => statuses.Contains(o.Status));
        }

        if (request.Visibility is { } visibility)
        {
            openings = openings.Where(o => o.Visibility == visibility);
        }

        if (request.RequisitionId is { } requisitionId)
        {
            openings = openings.Where(o => o.RequisitionId == JobRequisitionId.From(requisitionId));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = TextSearch.ContainsPattern(request.Search);
            openings = openings.Where(o => TextSearch.Matches(o.Title, pattern));
        }

        var page = await GetOpeningsQuery.Sorts.Apply(openings, request.Paging.Sort, GetOpeningsQuery.DefaultSort)
            .ToPagedResultAsync(request.Paging, cancellationToken);
        return new PagedResult<OpeningListItem>(await rows.RowsAsync(page.Items, cancellationToken), page.Page, page.PageSize, page.TotalCount);
    }
}

internal sealed class GetOpeningQueryHandler(
    IRecruitmentDbContext db,
    IAccessScopeProvider scopes,
    ICurrentUser currentUser,
    IUserDirectory users,
    OpeningRows rows) : IQueryHandler<GetOpeningQuery, OpeningDetails>
{
    public async Task<Result<OpeningDetails>> Handle(GetOpeningQuery request, CancellationToken cancellationToken)
    {
        var found = await db.JobOpenings.AsNoTracking()
            .Where(o => o.Id == JobOpeningId.From(request.Id))
            .Select(o => new { Opening = o, Version = EF.Property<uint>(o, ConcurrencyVersion.PropertyName) })
            .SingleOrDefaultAsync(cancellationToken);
        if (found is null || !await RequisitionScope.CoversAsync(scopes, request.Permission, found.Opening.CompanyId, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        var opening = found.Opening;
        var manages = await RequisitionScope.CoversAsync(scopes, RecruitmentPermissions.Openings.Manage, opening.CompanyId, cancellationToken);
        var team = opening.Team;
        var names = (await users.GetAsync([.. new[] { team.HiringManagerUserId }.Concat(team.RecruiterUserIds).Concat(team.InterviewerUserIds).Distinct()], cancellationToken))
            .ToDictionary(u => u.Id, u => u.DisplayName);
        OpeningMember Member(Guid id) => new(id, names.GetValueOrDefault(id));
        return new OpeningDetails(
            (await rows.RowsAsync([opening], cancellationToken)).Single(),
            opening.Description,
            opening.Qualifications,
            opening.BranchId,
            opening.StageTemplateId.Value,
            [.. opening.Stages.Select(s => new OpeningStageItem(s.Id, s.Name.Get(currentUser.Culture, "tr-TR"), s.Type, s.SlaDays))],
            Member(team.HiringManagerUserId),
            [.. team.RecruiterUserIds.Select(Member)],
            [.. team.InterviewerUserIds.Select(Member)],
            opening.Form,
            opening.PublishedAt,
            opening.ClosedAt,
            opening.CloseReason,
            manages && opening.Status != OpeningStatus.Closed,
            manages && opening.Status == OpeningStatus.Draft,
            manages && opening.Status is OpeningStatus.Draft or OpeningStatus.OnHold,
            manages && opening.Status == OpeningStatus.Published,
            manages && opening.Status != OpeningStatus.Closed,
            found.Version);
    }
}

internal sealed class GetOpeningTemplatesQueryHandler(IRecruitmentDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetOpeningTemplatesQuery, IReadOnlyList<OpeningTemplateOption>>
{
    public async Task<Result<IReadOnlyList<OpeningTemplateOption>>> Handle(GetOpeningTemplatesQuery request, CancellationToken cancellationToken)
    {
        var templates = await db.StageTemplates.AsNoTracking().Where(t => t.IsActive).ToListAsync(cancellationToken);
        return templates
            .OrderByDescending(t => t.IsDefault)
            .ThenBy(t => t.Name.Get(currentUser.Culture, "tr-TR"), StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo(currentUser.Culture), ignoreCase: true))
            .Select(t => new OpeningTemplateOption(
                t.Id.Value,
                t.Name.Get(currentUser.Culture, "tr-TR"),
                t.IsDefault,
                [.. t.Stages.Select(s => new OpeningStageItem(s.Id, s.Name.Get(currentUser.Culture, "tr-TR"), s.Type, s.SlaDays))]))
            .ToList();
    }
}

internal sealed class GetOpeningBranchesQueryHandler(IAccessScopeProvider scopes, IOrganizationLookup organization)
    : IQueryHandler<GetOpeningBranchesQuery, IReadOnlyList<BranchSummary>>
{
    public async Task<Result<IReadOnlyList<BranchSummary>>> Handle(GetOpeningBranchesQuery request, CancellationToken cancellationToken) =>
        await RequisitionScope.CoversAsync(scopes, request.Permission, request.CompanyId, cancellationToken)
            ? (await organization.GetBranchesOfCompaniesAsync([request.CompanyId], cancellationToken)).Where(b => b.IsActive).OrderBy(b => b.Name, StringComparer.Ordinal).ToList()
            : CommonErrors.Forbidden;
}

internal sealed class SearchOpeningUsersQueryHandler(IUserDirectory users) : IQueryHandler<SearchOpeningUsersQuery, IReadOnlyList<RequisitionUserOption>>
{
    private const int MaxResults = 20;

    public async Task<Result<IReadOnlyList<RequisitionUserOption>>> Handle(SearchOpeningUsersQuery request, CancellationToken cancellationToken) =>
        (await users.SearchAsync(request.Search, MaxResults, cancellationToken))
            .Where(u => u.IsActive)
            .Select(u => new RequisitionUserOption(u.Id, u.DisplayName, u.UserName))
            .ToList();
}
