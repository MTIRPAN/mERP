using System.Text.Json.Serialization;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Recruitment.Domain.Interviews;

/// <summary>A criterion scored 1–5; each score has its description, and the weight counts in the average (PLAN R5).</summary>
/// <param name="Id">Stable id; scores point to it.</param>
/// <param name="Name">What is assessed, e.g. "Problem çözme".</param>
/// <param name="ScoreDescriptions">What scores 1 to 5 mean, in order; empty when not described.</param>
/// <param name="Weight">Weight in the average, 1–100.</param>
public sealed record EvaluationCriterion(Guid Id, string Name, IReadOnlyList<string> ScoreDescriptions, decimal Weight)
{
    public const int NameMaxLength = 150;
    public const int DescriptionMaxLength = 300;
    public const decimal MaxWeight = 100;
}

/// <summary>A section of the form with its criteria.</summary>
public sealed record EvaluationSection(string Title, IReadOnlyList<EvaluationCriterion> Criteria)
{
    public const int TitleMaxLength = 150;
}

/// <summary>An open question the interviewer answers in words.</summary>
public sealed record EvaluationQuestion(Guid Id, string Text)
{
    public const int TextMaxLength = 300;
}

/// <summary>
/// What an interviewer fills in (PLAN R5, D52): sections of criteria scored 1–5 with weights, and open questions. An
/// interview copies its template's form, so later changes of the template do not touch it.
/// </summary>
public sealed record EvaluationForm(IReadOnlyList<EvaluationSection> Sections, IReadOnlyList<EvaluationQuestion> Questions)
{
    public const int MaxCriteria = 30;
    public const int MaxQuestions = 20;
    public const int Scores = 5;

    [JsonIgnore]
    public IEnumerable<EvaluationCriterion> Criteria => Sections.SelectMany(s => s.Criteria);

    /// <summary>
    /// At least one criterion; every name filled; five score descriptions each; weights 1–100; new criteria and
    /// questions (empty id) get one.
    /// </summary>
    public static Result<EvaluationForm> Check(EvaluationForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var criteria = form.Sections.SelectMany(s => s.Criteria).ToList();
        if (criteria.Count == 0 || criteria.Count > MaxCriteria || form.Questions.Count > MaxQuestions
            || form.Sections.Any(s => string.IsNullOrWhiteSpace(s.Title) || s.Criteria.Count == 0)
            || criteria.Any(c => string.IsNullOrWhiteSpace(c.Name) || c.ScoreDescriptions.Count != Scores || c.Weight is <= 0 or > EvaluationCriterion.MaxWeight)
            || form.Questions.Any(q => string.IsNullOrWhiteSpace(q.Text)))
        {
            return InterviewErrors.InvalidForm;
        }

        var checkedForm = new EvaluationForm(
            [
                .. form.Sections.Select(s => new EvaluationSection(
                    s.Title.Trim(),
                    [.. s.Criteria.Select(c => c with { Id = c.Id == Guid.Empty ? Guid.CreateVersion7() : c.Id, Name = c.Name.Trim(), ScoreDescriptions = [.. c.ScoreDescriptions.Select(d => d?.Trim() ?? string.Empty)] })])),
            ],
            [.. form.Questions.Select(q => q with { Id = q.Id == Guid.Empty ? Guid.CreateVersion7() : q.Id, Text = q.Text.Trim() })]);
        var ids = checkedForm.Criteria.Select(c => c.Id).Concat(checkedForm.Questions.Select(q => q.Id)).ToList();
        return ids.Distinct().Count() == ids.Count ? checkedForm : InterviewErrors.InvalidForm;
    }
}
