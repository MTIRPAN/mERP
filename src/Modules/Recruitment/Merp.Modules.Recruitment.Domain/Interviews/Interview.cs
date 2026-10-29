using System.Text.Json.Serialization;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.Modules.Recruitment.Domain.Candidates;
using Merp.Modules.Recruitment.Domain.Openings;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Recruitment.Domain.Interviews;

public readonly record struct InterviewId(Guid Value) : IEntityId<InterviewId>
{
    public static InterviewId From(Guid value) => new(value);
}

[JsonConverter(typeof(JsonStringEnumConverter<InterviewType>))]
public enum InterviewType
{
    Phone,
    Video,
    InPerson,
    Technical,
    Hr,
    Manager,
    Panel,
}

[JsonConverter(typeof(JsonStringEnumConverter<InterviewStatus>))]
public enum InterviewStatus
{
    Scheduled,
    Completed,
    Cancelled,

    /// <summary>The candidate did not come.</summary>
    NoShow,
}

/// <summary>When, where and with whom an interview takes place.</summary>
/// <param name="Type">Phone, video, in person, technical, HR, manager or panel.</param>
/// <param name="StartsAt">Start.</param>
/// <param name="EndsAt">End, after the start and at most 12 hours later.</param>
/// <param name="Location">The place of an interview in person.</param>
/// <param name="OnlineLink">The meeting link (http or https), entered by hand.</param>
/// <param name="InterviewerUserIds">One to ten active users.</param>
/// <param name="Notes">For the interviewers; the candidate does not see them.</param>
public sealed record InterviewTerms(
    InterviewType Type,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Location,
    string? OnlineLink,
    IReadOnlyList<Guid> InterviewerUserIds,
    string? Notes);

/// <summary>
/// An interview of an application (mülakat, PLAN R5, D52): its time, place and interviewers, and the evaluation form
/// copied from a template. Invitations with a calendar file go to the candidate and the interviewers; every change
/// raises <see cref="Sequence"/>, so calendars replace the earlier invitation. Reminders go a day and an hour before.
/// </summary>
[Audited]
public sealed class Interview : AuditableAggregateRoot<InterviewId>, ICompanyScoped
{
    public const int LocationMaxLength = 300;
    public const int LinkMaxLength = 500;
    public const int NotesMaxLength = 2000;
    public const int ReasonMaxLength = 500;
    public const int MaxInterviewers = 10;
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(12);

    private Interview()
    {
    }

    public JobApplicationId ApplicationId { get; private set; }

    public CandidateId CandidateId { get; private set; }

    public JobOpeningId OpeningId { get; private set; }

    public Guid CompanyId { get; private set; }

    public InterviewType Type { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    public string? Location { get; private set; }

    public string? OnlineLink { get; private set; }

    public IReadOnlyList<Guid> InterviewerUserIds { get; private set; } = [];

    public Guid OrganizerUserId { get; private set; }

    public string? Notes { get; private set; }

    public InterviewStatus Status { get; private set; }

    public string? CancelReason { get; private set; }

    /// <summary>The form the interviewers fill in; null when the interview is not evaluated with a form.</summary>
    public EvaluationForm? Evaluation { get; private set; }

    /// <summary>The iCalendar SEQUENCE of the invitation; every change raises it.</summary>
    public int Sequence { get; private set; }

    public DateTimeOffset? DayReminderSentAt { get; private set; }

    public DateTimeOffset? HourReminderSentAt { get; private set; }

    /// <summary>Plans an interview of an application in the pipeline.</summary>
    public static Result<Interview> Schedule(JobApplication application, InterviewTerms terms, EvaluationForm? evaluation, Guid organizerUserId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (application.Status != ApplicationStatus.Active)
        {
            return InterviewErrors.ApplicationNotActive;
        }

        var interview = new Interview
        {
            Id = EntityId.New<InterviewId>(),
            ApplicationId = application.Id,
            CandidateId = application.CandidateId,
            OpeningId = application.OpeningId,
            CompanyId = application.CompanyId,
            OrganizerUserId = organizerUserId,
            Status = InterviewStatus.Scheduled,
            Evaluation = evaluation,
        };
        var set = interview.Set(terms, now);
        return set.IsFailure ? set.Error : interview;
    }

    /// <summary>Changes a planned interview; the invitation is sent again as an update.</summary>
    public Result Reschedule(InterviewTerms terms, DateTimeOffset now)
    {
        if (Status != InterviewStatus.Scheduled)
        {
            return InterviewErrors.NotScheduled;
        }

        var set = Set(terms, now);
        if (set.IsSuccess)
        {
            Sequence++;
        }

        return set;
    }

    public Result Cancel(string? reason)
    {
        if (Status != InterviewStatus.Scheduled)
        {
            return InterviewErrors.NotScheduled;
        }

        Status = InterviewStatus.Cancelled;
        CancelReason = Text.Optional(reason, ReasonMaxLength, nameof(reason));
        Sequence++;
        return Result.Success();
    }

    /// <summary>The interview took place; the interviewers fill in their forms.</summary>
    public Result Complete() => Close(InterviewStatus.Completed);

    public Result MarkNoShow() => Close(InterviewStatus.NoShow);

    public bool IsInterviewer(Guid? userId) => userId is { } id && InterviewerUserIds.Contains(id);

    public void DayReminderSent(DateTimeOffset at) => DayReminderSentAt = at;

    public void HourReminderSent(DateTimeOffset at) => HourReminderSentAt = at;

    private Result Close(InterviewStatus status)
    {
        if (Status != InterviewStatus.Scheduled)
        {
            return InterviewErrors.NotScheduled;
        }

        Status = status;
        return Result.Success();
    }

    private Result Set(InterviewTerms terms, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(terms);
        var interviewers = terms.InterviewerUserIds.Distinct().ToList();
        if (terms.EndsAt <= terms.StartsAt || terms.EndsAt - terms.StartsAt > MaxDuration)
        {
            return InterviewErrors.InvalidTime;
        }

        if (interviewers.Count is 0 or > MaxInterviewers)
        {
            return InterviewErrors.Interviewers;
        }

        // A new time needs new reminders; none goes for a moment already within its reach
        if (terms.StartsAt != StartsAt)
        {
            DayReminderSentAt = terms.StartsAt - now <= TimeSpan.FromDays(1) ? now : null;
            HourReminderSentAt = terms.StartsAt - now <= TimeSpan.FromHours(1) ? now : null;
        }

        Type = terms.Type;
        StartsAt = terms.StartsAt;
        EndsAt = terms.EndsAt;
        Location = Text.Optional(terms.Location, LocationMaxLength, nameof(terms.Location));
        OnlineLink = Text.Optional(terms.OnlineLink, LinkMaxLength, nameof(terms.OnlineLink));
        InterviewerUserIds = interviewers;
        Notes = Text.Optional(terms.Notes, NotesMaxLength, nameof(terms.Notes));
        return Result.Success();
    }
}

public static class InterviewErrors
{
    public static readonly Error ApplicationNotActive = Error.Conflict("recruitment.interviews.errors.application_not_active");
    public static readonly Error NotScheduled = Error.Conflict("recruitment.interviews.errors.not_scheduled");
    public static readonly Error InvalidTime = Error.Validation([new FieldError("endsAt", "recruitment.interviews.errors.invalid_time")]);
    public static readonly Error Interviewers = Error.Validation([new FieldError("interviewerUserIds", "recruitment.interviews.errors.interviewers")]);
    public static readonly Error InvalidForm = Error.Validation([new FieldError("form", "recruitment.interviews.errors.invalid_form")]);
    public static readonly Error NotInterviewer = Error.Forbidden("recruitment.interviews.errors.not_interviewer");
    public static readonly Error Cancelled = Error.Conflict("recruitment.interviews.errors.cancelled");
    public static readonly Error NoForm = Error.Conflict("recruitment.interviews.errors.no_form");
    public static readonly Error InvalidScores = Error.Validation([new FieldError("scores", "recruitment.interviews.errors.invalid_scores")]);
}
