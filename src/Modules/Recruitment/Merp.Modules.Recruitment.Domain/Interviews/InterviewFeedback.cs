using System.Text.Json.Serialization;
using Merp.Modules.Recruitment.Domain.Applications;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Recruitment.Domain.Interviews;

public readonly record struct InterviewFeedbackId(Guid Value) : IEntityId<InterviewFeedbackId>
{
    public static InterviewFeedbackId From(Guid value) => new(value);
}

/// <summary>The interviewer's overall advice (PLAN R5).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Recommendation>))]
public enum Recommendation
{
    StrongNo,
    No,
    Yes,
    StrongYes,
}

/// <summary>A criterion's score, 1–5, with an optional comment.</summary>
public sealed record CriterionScore(Guid CriterionId, int Score, string? Comment)
{
    public const int CommentMaxLength = 1000;
}

/// <summary>An open question's answer.</summary>
public sealed record QuestionAnswer(Guid QuestionId, string Text)
{
    public const int TextMaxLength = 4000;
}

/// <summary>
/// An interviewer's form for an interview (geri bildirim, PLAN R5, D52): every criterion of the interview's form scored
/// 1–5, the open questions answered as they like, an overall advice. One per interviewer and interview; the
/// interviewer may correct it. The weighted score is kept for the application's average.
/// </summary>
[Audited]
public sealed class InterviewFeedback : AuditableAggregateRoot<InterviewFeedbackId>
{
    public const int SummaryMaxLength = 4000;

    private InterviewFeedback()
    {
    }

    public InterviewId InterviewId { get; private set; }

    public JobApplicationId ApplicationId { get; private set; }

    public Guid InterviewerUserId { get; private set; }

    public IReadOnlyList<CriterionScore> Scores { get; private set; } = [];

    public IReadOnlyList<QuestionAnswer> Answers { get; private set; } = [];

    public Recommendation Recommendation { get; private set; }

    public string? Summary { get; private set; }

    /// <summary>Σ(score × weight) / Σ(weight), 1–5 with two decimals.</summary>
    public decimal WeightedScore { get; private set; }

    public DateTimeOffset SubmittedAt { get; private set; }

    public static Result<InterviewFeedback> Submit(
        Interview interview,
        Guid interviewerUserId,
        IReadOnlyList<CriterionScore> scores,
        IReadOnlyList<QuestionAnswer> answers,
        Recommendation recommendation,
        string? summary,
        DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(interview);
        var feedback = new InterviewFeedback
        {
            Id = EntityId.New<InterviewFeedbackId>(),
            InterviewId = interview.Id,
            ApplicationId = interview.ApplicationId,
            InterviewerUserId = interviewerUserId,
        };
        var set = feedback.Set(interview, interviewerUserId, scores, answers, recommendation, summary, at);
        return set.IsFailure ? set.Error : feedback;
    }

    /// <summary>The interviewer corrects their form.</summary>
    public Result Correct(Interview interview, IReadOnlyList<CriterionScore> scores, IReadOnlyList<QuestionAnswer> answers, Recommendation recommendation, string? summary, DateTimeOffset at) =>
        Set(interview, InterviewerUserId, scores, answers, recommendation, summary, at);

    /// <summary>The weighted average of scores against the criteria of a form, rounded to two decimals.</summary>
    public static decimal Weigh(EvaluationForm form, IReadOnlyList<CriterionScore> scores)
    {
        ArgumentNullException.ThrowIfNull(form);
        var weights = form.Criteria.ToDictionary(c => c.Id, c => c.Weight);
        var total = scores.Sum(s => weights[s.CriterionId]);
        return Math.Round(scores.Sum(s => s.Score * weights[s.CriterionId]) / total, 2);
    }

    private Result Set(
        Interview interview,
        Guid interviewerUserId,
        IReadOnlyList<CriterionScore> scores,
        IReadOnlyList<QuestionAnswer> answers,
        Recommendation recommendation,
        string? summary,
        DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(answers);
        if (!interview.IsInterviewer(interviewerUserId))
        {
            return InterviewErrors.NotInterviewer;
        }

        if (interview.Status is InterviewStatus.Cancelled or InterviewStatus.NoShow)
        {
            return InterviewErrors.Cancelled;
        }

        if (interview.Evaluation is not { } form)
        {
            return InterviewErrors.NoForm;
        }

        // Every criterion once, scored 1–5; answers only to the form's questions
        var criteria = form.Criteria.Select(c => c.Id).ToHashSet();
        var questions = form.Questions.Select(q => q.Id).ToHashSet();
        if (scores.Count != criteria.Count
            || scores.Select(s => s.CriterionId).Distinct().Count() != scores.Count
            || scores.Any(s => !criteria.Contains(s.CriterionId) || s.Score is < 1 or > EvaluationForm.Scores)
            || answers.Any(a => !questions.Contains(a.QuestionId)))
        {
            return InterviewErrors.InvalidScores;
        }

        Scores = [.. scores.Select(s => s with { Comment = Text.Optional(s.Comment, CriterionScore.CommentMaxLength, nameof(s.Comment)) })];
        Answers = [.. answers.Where(a => !string.IsNullOrWhiteSpace(a.Text)).Select(a => a with { Text = Text.Required(a.Text, QuestionAnswer.TextMaxLength, nameof(a.Text)) })];
        Recommendation = Enum.IsDefined(recommendation) ? recommendation : throw new ArgumentOutOfRangeException(nameof(recommendation), recommendation, "Unknown advice.");
        Summary = Text.Optional(summary, SummaryMaxLength, nameof(summary));
        WeightedScore = Weigh(form, Scores);
        SubmittedAt = at;
        return Result.Success();
    }
}
