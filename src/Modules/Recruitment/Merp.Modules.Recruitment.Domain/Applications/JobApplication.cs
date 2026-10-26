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

/// <summary>Where an application stands; R4 moves it through the pipeline.</summary>
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
/// An application of a candidate to an opening (başvuru, PLAN R3, D49). It starts at the opening's Applied stage,
/// keeps the answers and whether a knockout answer missed; the candidate is registered or refreshed by
/// <see cref="ApplicationSubmitted"/> (rule 7).
/// </summary>
[Audited]
public sealed class JobApplication : AuditableAggregateRoot<JobApplicationId>, ICompanyScoped
{
    public const int CoverLetterMaxLength = 4000;

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
        };
        application.Raise(new ApplicationSubmitted(application.Id, terms.CandidateId, email, candidate, employeeId, terms.CvFileId, terms.Consent.PoolConsentRevision, terms.Source, at));
        return application;
    }
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
}
