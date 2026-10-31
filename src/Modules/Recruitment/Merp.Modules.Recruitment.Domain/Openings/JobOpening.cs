using System.Text.Json.Serialization;
using Merp.Modules.Recruitment.Domain.Requisitions;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Recruitment.Domain.Openings;

public readonly record struct JobOpeningId(Guid Value) : IEntityId<JobOpeningId>
{
    public static JobOpeningId From(Guid value) => new(value);
}

[JsonConverter(typeof(JsonStringEnumConverter<WorkModel>))]
public enum WorkModel
{
    OnSite,
    Remote,
    Hybrid,
}

/// <summary>Who sees an opening (D49).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OpeningVisibility>))]
public enum OpeningVisibility
{
    /// <summary>Employees only (self service).</summary>
    Internal,

    /// <summary>The career page and the employees.</summary>
    Public,
}

/// <summary>Where an opening stands (D49).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OpeningStatus>))]
public enum OpeningStatus
{
    Draft,

    /// <summary>Takes applications within its dates.</summary>
    Published,

    /// <summary>Paused: hidden, takes no applications, may be published again.</summary>
    OnHold,

    /// <summary>Done; never changes again.</summary>
    Closed,

    /// <summary>Every seat was hired for (D54): takes no applications; its pipeline is still worked on, it may be closed.</summary>
    Filled,
}

[JsonConverter(typeof(JsonStringEnumConverter<QuestionType>))]
public enum QuestionType
{
    Text,
    LongText,
    YesNo,
    SingleChoice,
    MultiChoice,
    Number,
}

/// <summary>A question of the application form (D49).</summary>
/// <param name="Id">Stable id; answers point to it.</param>
/// <param name="Text">The question in the opening's language.</param>
/// <param name="Type">How it is answered.</param>
/// <param name="Required">An answer is needed.</param>
/// <param name="Options">The choices of a choice question.</param>
/// <param name="AcceptedAnswers">
/// A knockout question's acceptable answers (options, or <c>yes</c>/<c>no</c>); an answer outside them marks the
/// application. Null for an ordinary question.
/// </param>
public sealed record OpeningQuestion(Guid Id, string Text, QuestionType Type, bool Required, IReadOnlyList<string> Options, IReadOnlyList<string>? AcceptedAnswers)
{
    public const int TextMaxLength = 300;
    public const int OptionMaxLength = 100;
    public const int MaxOptions = 20;
    public const string Yes = "yes";
    public const string No = "no";

    public bool IsChoice => Type is QuestionType.SingleChoice or QuestionType.MultiChoice;

    public bool IsKnockout => AcceptedAnswers is { Count: > 0 };
}

/// <summary>What the application form asks besides name and e-mail, which it always asks (D49).</summary>
public sealed record ApplicationForm(bool RequirePhone, bool RequireCv, bool AskCoverLetter, IReadOnlyList<OpeningQuestion> Questions)
{
    public const int MaxQuestions = 20;

    public static ApplicationForm Default { get; } = new(RequirePhone: true, RequireCv: true, AskCoverLetter: false, []);
}

/// <summary>The people who hire for an opening (D49); recruiters and interviewers reach its candidates (R4, R5).</summary>
public sealed record OpeningTeam(Guid HiringManagerUserId, IReadOnlyList<Guid> RecruiterUserIds, IReadOnlyList<Guid> InterviewerUserIds)
{
    public const int MaxMembers = 30;
}

/// <summary>What an opening says, as the application checked it. Rich texts are already sanitized.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="RequisitionId">The approved requisition the opening fills; null for none.</param>
/// <param name="Title">The job title shown.</param>
/// <param name="Description">The job, as sanitized HTML.</param>
/// <param name="Qualifications">What is sought, as sanitized HTML.</param>
/// <param name="BranchId">Where the job is; a Platform branch of the company.</param>
/// <param name="WorkModel">On site, remote or hybrid.</param>
/// <param name="EmploymentType">The kind of employment.</param>
/// <param name="Headcount">People sought.</param>
/// <param name="Visibility">Employees only, or the career page too.</param>
/// <param name="PublishFrom">First day it is shown; today when published without one.</param>
/// <param name="PublishUntil">Last day it is shown.</param>
/// <param name="Team">Who hires.</param>
/// <param name="Form">What the application form asks.</param>
public sealed record OpeningTerms(
    Guid CompanyId,
    JobRequisitionId? RequisitionId,
    string Title,
    string? Description,
    string? Qualifications,
    Guid? BranchId,
    WorkModel WorkModel,
    RequisitionEmploymentType EmploymentType,
    int Headcount,
    OpeningVisibility Visibility,
    DateOnly? PublishFrom,
    DateOnly? PublishUntil,
    OpeningTeam Team,
    ApplicationForm Form);

/// <summary>
/// A job opening (iş ilanı, PLAN R3, D49): what is offered and asked, the pipeline copied from a stage template, the
/// hiring team and the application form. Published within its dates it takes applications; public ones are on the
/// career page.
/// </summary>
[Audited]
public sealed class JobOpening : AuditableAggregateRoot<JobOpeningId>, ICompanyScoped
{
    public const int TitleMaxLength = 150;
    public const int RichTextMaxLength = 20_000;
    public const int MaxHeadcount = 99;
    public const int ReasonMaxLength = 500;

    private JobOpening()
    {
    }

    public Guid CompanyId { get; private set; }

    public JobRequisitionId? RequisitionId { get; private set; }

    public string Title { get; private set; } = null!;

    public string? Description { get; private set; }

    public string? Qualifications { get; private set; }

    public Guid? BranchId { get; private set; }

    public WorkModel WorkModel { get; private set; }

    public RequisitionEmploymentType EmploymentType { get; private set; }

    public int Headcount { get; private set; }

    public OpeningVisibility Visibility { get; private set; }

    public DateOnly? PublishFrom { get; private set; }

    public DateOnly? PublishUntil { get; private set; }

    public Guid HiringManagerUserId { get; private set; }

    public OpeningTeam Team { get; private set; } = null!;

    public ApplicationForm Form { get; private set; } = null!;

    /// <summary>The template the stages were copied from.</summary>
    public StageTemplateId StageTemplateId { get; private set; }

    /// <summary>The pipeline, copied from the template (D47); stage ids are those of the template.</summary>
    public IReadOnlyList<Stage> Stages { get; private set; } = [];

    public OpeningStatus Status { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public string? CloseReason { get; private set; }

    /// <summary>When the texts last came from an AI draft (D57).</summary>
    public DateTimeOffset? AiDraftedAt { get; private set; }

    /// <summary>When a user last confirmed reviewing the AI draft, and who.</summary>
    public DateTimeOffset? AiDraftReviewedAt { get; private set; }

    public Guid? AiDraftReviewedByUserId { get; private set; }

    /// <summary>The texts came from an AI draft no one has confirmed reviewing since; the opening cannot be published.</summary>
    public bool AiDraftPending => AiDraftedAt is { } drafted && (AiDraftReviewedAt is not { } reviewed || reviewed < drafted);

    public static JobOpening Draft(OpeningTerms terms, StageTemplate template)
    {
        var opening = new JobOpening { Id = EntityId.New<JobOpeningId>(), Status = OpeningStatus.Draft };
        opening.Set(terms);
        opening.CopyStages(template);
        return opening;
    }

    /// <summary>
    /// Corrects an opening that is not closed. The company and the requisition never change; the stages change only
    /// in a draft (<paramref name="template"/> null keeps them).
    /// </summary>
    public Result Update(OpeningTerms terms, StageTemplate? template)
    {
        ArgumentNullException.ThrowIfNull(terms);
        if (Status == OpeningStatus.Closed)
        {
            return JobOpeningErrors.Closed;
        }

        if (terms.CompanyId != CompanyId || terms.RequisitionId != RequisitionId)
        {
            return JobOpeningErrors.FixedFields;
        }

        if (template is not null && template.Id != StageTemplateId && Status != OpeningStatus.Draft)
        {
            return JobOpeningErrors.StagesFixed;
        }

        Set(terms);
        if (template is not null && Status == OpeningStatus.Draft)
        {
            CopyStages(template);
        }

        return Result.Success();
    }

    /// <summary>The description or qualifications were filled from an AI draft (D57): a user has to review them.</summary>
    public void DraftedWithAi(DateTimeOffset at) => AiDraftedAt = at;

    /// <summary>A user confirms reviewing the AI draft; nothing changes when no draft waits for review.</summary>
    public void ConfirmAiDraftReviewed(Guid userId, DateTimeOffset at)
    {
        if (AiDraftPending)
        {
            AiDraftReviewedAt = at;
            AiDraftReviewedByUserId = userId;
        }
    }

    /// <summary>
    /// Publishes a draft or an opening on hold; without a first day it is shown from <paramref name="today"/>. An AI
    /// draft has to be reviewed first (D57).
    /// </summary>
    public Result Publish(DateOnly today, DateTimeOffset at)
    {
        if (Status is not (OpeningStatus.Draft or OpeningStatus.OnHold))
        {
            return JobOpeningErrors.NotPublishable;
        }

        if (AiDraftPending)
        {
            return JobOpeningErrors.AiDraftNotReviewed;
        }

        if (PublishUntil is { } until && until < today)
        {
            return JobOpeningErrors.Expired;
        }

        PublishFrom ??= today;
        Status = OpeningStatus.Published;
        PublishedAt ??= at;
        return Result.Success();
    }

    /// <summary>Pauses a published opening.</summary>
    public Result Hold()
    {
        if (Status != OpeningStatus.Published)
        {
            return JobOpeningErrors.NotPublished;
        }

        Status = OpeningStatus.OnHold;
        return Result.Success();
    }

    /// <summary>Closes an opening that is not closed yet.</summary>
    public Result Close(string? reason, DateTimeOffset at)
    {
        if (Status == OpeningStatus.Closed)
        {
            return JobOpeningErrors.Closed;
        }

        Status = OpeningStatus.Closed;
        CloseReason = Text.Optional(reason, ReasonMaxLength, nameof(reason));
        ClosedAt = at;
        return Result.Success();
    }

    /// <summary>
    /// Every seat was hired for (D54): a published or paused opening stops taking applications. Returns whether it
    /// changed.
    /// </summary>
    public bool MarkFilled(int hired, DateTimeOffset at)
    {
        if (hired < Headcount || Status is not (OpeningStatus.Published or OpeningStatus.OnHold))
        {
            return false;
        }

        Status = OpeningStatus.Filled;
        ClosedAt = at;
        return true;
    }

    /// <summary>
    /// Changes the pipeline of an opening that is not closed (D50), under the rules of a template. A stage that
    /// applications are at, or left the pipeline from, stays.
    /// </summary>
    public Result ChangeStages(IReadOnlyList<Stage> stages, IReadOnlySet<Guid> stagesInUse)
    {
        ArgumentNullException.ThrowIfNull(stagesInUse);
        if (Status == OpeningStatus.Closed)
        {
            return JobOpeningErrors.Closed;
        }

        var checkedStages = StageTemplate.CheckStages(stages);
        if (checkedStages.IsFailure)
        {
            return checkedStages.Error;
        }

        if (stagesInUse.Any(id => checkedStages.Value.All(s => s.Id != id)))
        {
            return JobOpeningErrors.StageInUse;
        }

        Stages = checkedStages.Value;
        return Result.Success();
    }

    /// <summary>Takes applications on <paramref name="today"/>: published and within its dates.</summary>
    public bool IsOpenOn(DateOnly today) =>
        Status == OpeningStatus.Published
        && (PublishFrom is null || PublishFrom <= today)
        && (PublishUntil is null || today <= PublishUntil);

    private void Set(OpeningTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(terms.Team);
        ArgumentNullException.ThrowIfNull(terms.Form);
        if (terms.Headcount is < 1 or > MaxHeadcount)
        {
            throw new ArgumentOutOfRangeException(nameof(terms), terms.Headcount, "An opening seeks 1 to 99 people.");
        }

        if (terms.PublishFrom is { } from && terms.PublishUntil is { } until && until < from)
        {
            throw new ArgumentException("The last day is before the first.", nameof(terms));
        }

        CompanyId = terms.CompanyId;
        RequisitionId = terms.RequisitionId;
        Title = Text.Required(terms.Title, TitleMaxLength, nameof(terms.Title));
        Description = Text.Optional(terms.Description, RichTextMaxLength, nameof(terms.Description));
        Qualifications = Text.Optional(terms.Qualifications, RichTextMaxLength, nameof(terms.Qualifications));
        BranchId = terms.BranchId;
        WorkModel = terms.WorkModel;
        EmploymentType = terms.EmploymentType;
        Headcount = terms.Headcount;
        Visibility = terms.Visibility;
        PublishFrom = terms.PublishFrom;
        PublishUntil = terms.PublishUntil;
        HiringManagerUserId = terms.Team.HiringManagerUserId;
        Team = terms.Team with
        {
            RecruiterUserIds = [.. terms.Team.RecruiterUserIds.Distinct()],
            InterviewerUserIds = [.. terms.Team.InterviewerUserIds.Distinct()],
        };
        Form = terms.Form with
        {
            Questions = [.. terms.Form.Questions.Select(q => q.Id == Guid.Empty ? q with { Id = Guid.CreateVersion7() } : q)],
        };
    }

    private void CopyStages(StageTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        StageTemplateId = template.Id;
        Stages = [.. template.Stages];
    }
}

public static class JobOpeningErrors
{
    public static readonly Error Closed = Error.Conflict("recruitment.openings.errors.closed");
    public static readonly Error NotPublishable = Error.Conflict("recruitment.openings.errors.not_publishable");
    public static readonly Error NotPublished = Error.Conflict("recruitment.openings.errors.not_published");
    public static readonly Error AiDraftNotReviewed = Error.Conflict("recruitment.openings.errors.ai_draft_not_reviewed");
    public static readonly Error Expired = Error.Validation([new FieldError("publishUntil", "recruitment.openings.errors.expired")]);
    public static readonly Error StagesFixed = Error.Validation([new FieldError("stageTemplateId", "recruitment.openings.errors.stages_fixed")]);
    public static readonly Error StageInUse = Error.Validation([new FieldError("stages", "recruitment.openings.errors.stage_in_use")]);
    public static readonly Error FixedFields =Error.Validation([new FieldError("companyId", "recruitment.openings.errors.fixed_fields")]);
}
