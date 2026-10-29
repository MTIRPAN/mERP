using Merp.SharedKernel.Domain;

namespace Merp.Modules.Recruitment.Domain.Applications;

/// <summary>
/// The AI analysis of an application's CV (PLAN A3, D51): a suggestion for the team, never a decision. One per
/// application, keyed by it; a new analysis replaces the previous one. The analysis is kept as the JSON the
/// application layer produced, with the model and the prompt version; a failed run keeps only its error code.
/// </summary>
public sealed class ApplicationCvAnalysis : AuditableAggregateRoot<JobApplicationId>
{
    public const int ErrorCodeMaxLength = 100;
    public const int ModelMaxLength = 100;
    public const int PromptVersionMaxLength = 50;

    private ApplicationCvAnalysis()
    {
    }

    /// <summary>The CV analyzed (a stored file of the candidate).</summary>
    public Guid FileId { get; private set; }

    /// <summary>The analysis as JSON; null when the run failed.</summary>
    public string? Result { get; private set; }

    /// <summary>Why the run failed, as an API error code (e.g. <c>common.errors.ai_budget_exceeded</c>); null on success.</summary>
    public string? ErrorCode { get; private set; }

    public string? Model { get; private set; }

    public string? PromptVersion { get; private set; }

    /// <summary>Who asked for it; null for the background job.</summary>
    public Guid? RequestedByUserId { get; private set; }

    public DateTimeOffset AnalyzedAt { get; private set; }

    public bool Succeeded => Result is not null;

    public static ApplicationCvAnalysis Of(JobApplicationId applicationId) => new() { Id = applicationId };

    public void Succeed(Guid fileId, string result, string model, string promptVersion, Guid? userId, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(result);
        Set(fileId, userId, at);
        Result = result;
        ErrorCode = null;
        Model = Text.Required(model, ModelMaxLength, nameof(model));
        PromptVersion = Text.Required(promptVersion, PromptVersionMaxLength, nameof(promptVersion));
    }

    public void Fail(Guid fileId, string errorCode, Guid? userId, DateTimeOffset at)
    {
        Set(fileId, userId, at);
        Result = null;
        ErrorCode = Text.Required(errorCode, ErrorCodeMaxLength, nameof(errorCode));
        Model = null;
        PromptVersion = null;
    }

    private void Set(Guid fileId, Guid? userId, DateTimeOffset at)
    {
        FileId = fileId == Guid.Empty ? throw new ArgumentException("A CV is required.", nameof(fileId)) : fileId;
        RequestedByUserId = userId;
        AnalyzedAt = at;
    }
}
