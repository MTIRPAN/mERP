using System.Globalization;
using System.Text.Json.Serialization;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.Modules.Recruitment.Domain.Settings;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Recruitment.Domain.Applications;

public readonly record struct JobApplicationId(Guid Value) : IEntityId<JobApplicationId>
{
    public static JobApplicationId From(Guid value) => new(value);
}

/// <summary>Where an application stands (D50): in the pipeline, or out of it at the final stage of the same name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ApplicationStatus>))]
public enum ApplicationStatus
{
    /// <summary>In the pipeline, at <see cref="JobApplication.StageId"/>.</summary>
    Active,

    Hired,

    Rejected,

    Withdrawn,
}

/// <summary>The answer to a question of the form: one value, several for a multiple choice.</summary>
public sealed record ApplicationAnswer(Guid QuestionId, IReadOnlyList<string> Values)
{
    public const int TextMaxLength = 500;
    public const int LongTextMaxLength = 4000;
}

/// <summary>The KVKK texts the candidate accepted, by their revisions (D47).</summary>
/// <param name="DisclosureRevision">The disclosure text shown; 0 is the built-in text.</param>
/// <param name="Culture">The language of the texts shown.</param>
/// <param name="PoolConsentRevision">The explicit consent text accepted to stay in the pool; null when not given.</param>
public sealed record ApplicationConsent(int DisclosureRevision, string Culture, int? PoolConsentRevision);

/// <summary>What an application gives, besides the candidate's contact details.</summary>
/// <param name="CandidateId">The candidate, existing or registered by the application.</param>
/// <param name="CoverLetter">Asked when the opening wants one.</param>
/// <param name="Answers">The answers to the opening's questions.</param>
/// <param name="CvFileId">The CV sent with it; null when none.</param>
/// <param name="Consent">The KVKK texts accepted.</param>
/// <param name="IsInternal">An employee applied from self service.</param>
/// <param name="Source">The candidate source code (D47 lists).</param>
public sealed record ApplicationTerms(
    CandidateId CandidateId,
    string? CoverLetter,
    IReadOnlyList<ApplicationAnswer> Answers,
    Guid? CvFileId,
    ApplicationConsent Consent,
    bool IsInternal,
    string Source);

/// <summary>
/// An application of a candidate to an opening (başvuru, PLAN R3–R4, D49, D50). It starts at the opening's Applied
/// stage, keeps the answers and whether a knockout answer missed; the candidate is registered or refreshed by
/// <see cref="ApplicationSubmitted"/> (rule 7). The team moves it through the working stages, rejects it, records
/// that the candidate withdrew, or reopens it; every change is written to <see cref="History"/>.
/// </summary>
[Audited]
public sealed class JobApplication : AuditableAggregateRoot<JobApplicationId>, ICompanyScoped
{
    public const int CoverLetterMaxLength = 4000;
    public const int NoteMaxLength = 1000;
    public const int MaxEmailDelayDays = 14;

    private readonly List<ApplicationStageChange> _history = [];

    private JobApplication()
    {
    }

    public CandidateId CandidateId { get; private set; }

    public JobOpeningId OpeningId { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>A stage of the opening's pipeline.</summary>
    public Guid StageId { get; private set; }

    public ApplicationStatus Status { get; private set; }

    public string? CoverLetter { get; private set; }

    public IReadOnlyList<ApplicationAnswer> Answers { get; private set; } = [];

    /// <summary>An answer to a knockout question was outside the accepted ones; the team decides (R4).</summary>
    public bool KnockedOut { get; private set; }

    public Guid? CvFileId { get; private set; }

    public ApplicationConsent Consent { get; private set; } = null!;

    public bool IsInternal { get; private set; }

    public string Source { get; private set; } = null!;

    public DateTimeOffset AppliedAt { get; private set; }

    /// <summary>When the application came to its current stage; the stage's SLA counts from it.</summary>
    public DateTimeOffset StageEnteredAt { get; private set; }

    /// <summary>The working stage the application left the pipeline from; reopening returns there.</summary>
    public Guid? PreviousStageId { get; private set; }

    /// <summary>The rejection or withdrawal reason code (D47 lists) while the application is out for one.</summary>
    public string? ReasonCode { get; private set; }

    /// <summary>When the rejection e-mail goes to the candidate; null when none is waiting.</summary>
    public DateTimeOffset? RejectionEmailDueAt { get; private set; }

    public DateTimeOffset? RejectionEmailSentAt { get; private set; }

    /// <summary>Every stage change, oldest first by <see cref="ApplicationStageChange.Sequence"/>.</summary>
    public IReadOnlyCollection<ApplicationStageChange> History => _history.AsReadOnly();

    /// <summary>
    /// Receives an application to an opening that takes applications on <paramref name="today"/>; the answers are
    /// checked against its form. <paramref name="candidate"/> goes with the event that registers or refreshes the
    /// candidate.
    /// </summary>
    public static Result<JobApplication> Submit(
        JobOpening opening,
        ApplicationTerms terms,
        string email,
        CandidateContact candidate,
        Guid? employeeId,
        DateOnly today,
        DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(opening);
        ArgumentNullException.ThrowIfNull(terms);
        if (!opening.IsOpenOn(today))
        {
            return JobApplicationErrors.OpeningClosed;
        }

        var answers = ApplicationAnswers.Check(opening.Form, terms.Answers);
        var missingCv = opening.Form.RequireCv && terms.CvFileId is null;
        if (answers.IsFailure || missingCv)
        {
            return Error.Validation([
                .. missingCv ? [new FieldError("cv", JobApplicationErrors.CvRequiredCode)] : Array.Empty<FieldError>(),
                .. answers.IsFailure ? answers.Error.Fields : [],
            ]);
        }

        var applied = opening.Stages.FirstOrDefault(s => s.Type == StageType.Applied)
            ?? throw new InvalidOperationException("An opening's pipeline starts with an Applied stage.");
        var application = new JobApplication
        {
            Id = EntityId.New<JobApplicationId>(),
            CandidateId = terms.CandidateId,
            OpeningId = opening.Id,
            CompanyId = opening.CompanyId,
            StageId = applied.Id,
            Status = ApplicationStatus.Active,
            CoverLetter = opening.Form.AskCoverLetter ? Text.Optional(terms.CoverLetter, CoverLetterMaxLength, nameof(terms.CoverLetter)) : null,
            Answers = answers.Value.Answers,
            KnockedOut = answers.Value.KnockedOut,
            CvFileId = terms.CvFileId,
            Consent = terms.Consent,
            IsInternal = terms.IsInternal,
            Source = Text.Required(terms.Source, Candidate.SourceMaxLength, nameof(terms.Source)),
            AppliedAt = at,
            StageEnteredAt = at,
        };
        application.Record(null, applied.Id, null, null, null, at);
        application.Raise(new ApplicationSubmitted(application.Id, terms.CandidateId, email, candidate, employeeId, terms.CvFileId, terms.Consent.PoolConsentRevision, terms.Source, at));
        return application;
    }

    /// <summary>
    /// Moves an application in the pipeline to a working stage of the opening, or to Hired. Rejection and withdrawal
    /// have their own methods since they need a reason.
    /// </summary>
    public Result MoveTo(JobOpening opening, Guid stageId, string? note, Guid? userId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(opening);
        if (Status != ApplicationStatus.Active)
        {
            return JobApplicationErrors.NotActive;
        }

        var stage = opening.Stages.FirstOrDefault(s => s.Id == stageId);
        if (stage is null)
        {
            return JobApplicationErrors.UnknownStage;
        }

        if (stage.Type is StageType.Rejected or StageType.Withdrawn)
        {
            return JobApplicationErrors.NeedsReason;
        }

        if (stage.Id == StageId)
        {
            return Result.Success();
        }

        if (stage.Type == StageType.Hired)
        {
            PreviousStageId = StageId;
            Status = ApplicationStatus.Hired;
        }

        Enter(stage.Id, null, note, userId, at);
        return Result.Success();
    }

    /// <summary>
    /// The company turns the candidate down for a reason. The rejection e-mail goes at <paramref name="emailDueAt"/>
    /// (at most 14 days later); null sends none.
    /// </summary>
    public Result Reject(JobOpening opening, string reasonCode, string? note, DateTimeOffset? emailDueAt, Guid? userId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(opening);
        if (emailDueAt is { } due && (due < at || due > at.AddDays(MaxEmailDelayDays)))
        {
            throw new ArgumentOutOfRangeException(nameof(emailDueAt), emailDueAt, "The e-mail goes within 14 days.");
        }

        var left = Leave(opening, StageType.Rejected, ApplicationStatus.Rejected, reasonCode, note, userId, at);
        if (left.IsSuccess)
        {
            RejectionEmailDueAt = emailDueAt;
            RejectionEmailSentAt = null;
        }

        return left;
    }

    /// <summary>The candidate withdrew, for a reason.</summary>
    public Result Withdraw(JobOpening opening, string reasonCode, string? note, Guid? userId, DateTimeOffset at) =>
        Leave(opening, StageType.Withdrawn, ApplicationStatus.Withdrawn, reasonCode, note, userId, at);

    /// <summary>
    /// Brings a rejected or withdrawn application back to the stage it left from (the Applied stage when that stage
    /// is gone). A rejection e-mail not sent yet is dropped.
    /// </summary>
    public Result Reopen(JobOpening opening, string? note, Guid? userId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(opening);
        if (Status is not (ApplicationStatus.Rejected or ApplicationStatus.Withdrawn))
        {
            return JobApplicationErrors.NotClosed;
        }

        var stage = opening.Stages.FirstOrDefault(s => s.Id == PreviousStageId && !s.IsFinal)
            ?? opening.Stages.First(s => s.Type == StageType.Applied);
        Status = ApplicationStatus.Active;
        ReasonCode = null;
        PreviousStageId = null;
        RejectionEmailDueAt = null;
        Enter(stage.Id, null, note, userId, at);
        return Result.Success();
    }

    /// <summary>The rejection e-mail went out.</summary>
    public void RejectionEmailSent(DateTimeOffset at)
    {
        RejectionEmailDueAt = null;
        RejectionEmailSentAt = at;
    }

    private Result Leave(JobOpening opening, StageType type, ApplicationStatus status, string reasonCode, string? note, Guid? userId, DateTimeOffset at)
    {
        if (Status != ApplicationStatus.Active)
        {
            return JobApplicationErrors.NotActive;
        }

        var stage = opening.Stages.First(s => s.Type == type);
        PreviousStageId = StageId;
        Status = status;
        ReasonCode = Text.Required(reasonCode, RecruitmentLookup.CodeMaxLength, nameof(reasonCode));
        Enter(stage.Id, ReasonCode, note, userId, at);
        return Result.Success();
    }

    private void Enter(Guid stageId, string? reasonCode, string? note, Guid? userId, DateTimeOffset at)
    {
        var from = StageId;
        StageId = stageId;
        StageEnteredAt = at;
        Record(from, stageId, reasonCode, note, userId, at);
    }

    private void Record(Guid? from, Guid to, string? reasonCode, string? note, Guid? userId, DateTimeOffset at) =>
        _history.Add(new ApplicationStageChange(Id, _history.Count + 1, from, to, Status, reasonCode, Text.Optional(note, NoteMaxLength, nameof(note)), userId, at));
}

/// <summary>
/// A step of an application through the pipeline (D50): from which stage to which, the status after it, the reason
/// of a rejection or withdrawal, who did it and when. The first entry is the application itself.
/// </summary>
public sealed class ApplicationStageChange
{
    private ApplicationStageChange()
    {
    }

    internal ApplicationStageChange(
        JobApplicationId applicationId,
        int sequence,
        Guid? fromStageId,
        Guid toStageId,
        ApplicationStatus status,
        string? reasonCode,
        string? note,
        Guid? userId,
        DateTimeOffset at)
    {
        Id = Guid.CreateVersion7();
        ApplicationId = applicationId;
        Sequence = sequence;
        FromStageId = fromStageId;
        ToStageId = toStageId;
        Status = status;
        ReasonCode = reasonCode;
        Note = note;
        UserId = userId;
        At = at;
    }

    /// <summary>Own key, so two concurrent moves conflict on the application version, not on the history.</summary>
    public Guid Id { get; private set; }

    public JobApplicationId ApplicationId { get; private set; }

    public int Sequence { get; private set; }

    /// <summary>Null for the entry of the application.</summary>
    public Guid? FromStageId { get; private set; }

    public Guid ToStageId { get; private set; }

    public ApplicationStatus Status { get; private set; }

    public string? ReasonCode { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Who moved it; null for the candidate and the system.</summary>
    public Guid? UserId { get; private set; }

    public DateTimeOffset At { get; private set; }
}

/// <summary>An application was received: the candidate is registered, or refreshed with its contact details and CV.</summary>
public sealed record ApplicationSubmitted(
    JobApplicationId ApplicationId,
    CandidateId CandidateId,
    string Email,
    CandidateContact Contact,
    Guid? EmployeeId,
    Guid? CvFileId,
    int? PoolConsentRevision,
    string Source,
    DateTimeOffset At) : IDomainEvent;

/// <summary>The answers of an application, checked against the form of the opening (D49).</summary>
public static class ApplicationAnswers
{
    public sealed record Checked(IReadOnlyList<ApplicationAnswer> Answers, bool KnockedOut);

    /// <summary>
    /// Every required question is answered and every answer fits its question; empty answers are dropped. A knockout
    /// question knocks the application out when any value given is outside the accepted answers.
    /// </summary>
    public static Result<Checked> Check(ApplicationForm form, IReadOnlyList<ApplicationAnswer>? answers)
    {
        ArgumentNullException.ThrowIfNull(form);
        var given = (answers ?? [])
            .GroupBy(a => a.QuestionId)
            .ToDictionary(g => g.Key, g => g.SelectMany(a => a.Values ?? []).Select(v => v?.Trim() ?? string.Empty).Where(v => v.Length > 0).ToList());
        var errors = new List<FieldError>();
        foreach (var unknown in given.Keys.Where(id => form.Questions.All(q => q.Id != id)))
        {
            errors.Add(new FieldError(Field(unknown), JobApplicationErrors.UnknownQuestionCode));
        }

        var kept = new List<ApplicationAnswer>();
        var knockedOut = false;
        foreach (var question in form.Questions)
        {
            var values = given.GetValueOrDefault(question.Id) ?? [];
            if (values.Count == 0)
            {
                if (question.Required)
                {
                    errors.Add(new FieldError(Field(question.Id), JobApplicationErrors.AnswerRequiredCode));
                }

                continue;
            }

            if (!Fits(question, values))
            {
                errors.Add(new FieldError(Field(question.Id), JobApplicationErrors.InvalidAnswerCode));
                continue;
            }

            if (question.IsKnockout && values.Any(v => !question.AcceptedAnswers!.Contains(v, StringComparer.Ordinal)))
            {
                knockedOut = true;
            }

            kept.Add(new ApplicationAnswer(question.Id, values));
        }

        return errors.Count > 0 ? Error.Validation(errors) : new Checked(kept, knockedOut);
    }

    /// <summary>The field of a question's answer in validation errors.</summary>
    public static string Field(Guid questionId) => $"answers.{questionId}";

    private static bool Fits(OpeningQuestion question, List<string> values) => question.Type switch
    {
        QuestionType.Text => values.Count == 1 && values[0].Length <= ApplicationAnswer.TextMaxLength,
        QuestionType.LongText => values.Count == 1 && values[0].Length <= ApplicationAnswer.LongTextMaxLength,
        QuestionType.YesNo => values.Count == 1 && values[0] is OpeningQuestion.Yes or OpeningQuestion.No,
        QuestionType.SingleChoice => values.Count == 1 && question.Options.Contains(values[0], StringComparer.Ordinal),
        QuestionType.MultiChoice => values.Distinct(StringComparer.Ordinal).Count() == values.Count
            && values.All(v => question.Options.Contains(v, StringComparer.Ordinal)),
        QuestionType.Number => values.Count == 1 && decimal.TryParse(values[0], NumberStyles.Number, CultureInfo.InvariantCulture, out _),
        _ => false,
    };
}

public static class JobApplicationErrors
{
    public const string CvRequiredCode = "recruitment.applications.errors.cv_required";
    public const string UnknownQuestionCode = "recruitment.applications.errors.unknown_question";
    public const string AnswerRequiredCode = "recruitment.applications.errors.answer_required";
    public const string InvalidAnswerCode = "recruitment.applications.errors.invalid_answer";

    public static readonly Error OpeningClosed = Error.Conflict("recruitment.applications.errors.opening_closed");
    public static readonly Error NotActive = Error.Conflict("recruitment.applications.errors.not_active");
    public static readonly Error NotClosed = Error.Conflict("recruitment.applications.errors.not_closed");
    public static readonly Error UnknownStage = Error.Validation([new FieldError("stageId", "recruitment.applications.errors.unknown_stage")]);
    public static readonly Error NeedsReason = Error.Validation([new FieldError("stageId", "recruitment.applications.errors.needs_reason")]);
}
