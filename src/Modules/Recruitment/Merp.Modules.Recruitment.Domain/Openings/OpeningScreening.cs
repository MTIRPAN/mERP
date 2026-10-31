using System.Text.Json.Serialization;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Recruitment.Domain.Openings;

public readonly record struct OpeningScreeningId(Guid Value) : IEntityId<OpeningScreeningId>
{
    public static OpeningScreeningId From(Guid value) => new(value);
}

[JsonConverter(typeof(JsonStringEnumConverter<ScreeningStatus>))]
public enum ScreeningStatus
{
    /// <summary>Waiting for the night run.</summary>
    Queued,

    /// <summary>Sent to the provider's batch processing; the results are collected when it ends.</summary>
    Submitted,

    /// <summary>Not sent last night, e.g. over the budget; tried again the next night.</summary>
    Held,

    Completed,

    Cancelled,
}

/// <summary>An application sent in a batch, with what its stored analysis needs besides the model's output.</summary>
/// <param name="ApplicationId">The application.</param>
/// <param name="FileId">The CV analyzed.</param>
/// <param name="Redactions">How many values of each kind were removed before sending (kind code → count).</param>
/// <param name="Truncated">The CV was longer than what is read.</param>
public sealed record ScreeningItem(Guid ApplicationId, Guid FileId, IReadOnlyDictionary<string, int> Redactions, bool Truncated);

/// <summary>
/// A batch screening of an opening's applications (toplu ön tarama, PLAN A6, D59). HR asks for it; the night run sends
/// the applications without a current CV analysis to the provider's batch processing at half price, and the results
/// are stored as the applications' CV analyses. When a night's limit leaves some out, the screening goes on the next
/// night. It never ranks or rejects anyone.
/// </summary>
[Audited]
public sealed class OpeningScreening : AuditableAggregateRoot<OpeningScreeningId>, ICompanyScoped
{
    public const int BatchIdMaxLength = 100;
    public const int ReasonMaxLength = 100;
    public const int RequirementsHashLength = 64;

    private OpeningScreening()
    {
    }

    public JobOpeningId OpeningId { get; private set; }

    public Guid CompanyId { get; private set; }

    public ScreeningStatus Status { get; private set; }

    public Guid RequestedByUserId { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>The provider's id of the batch running now; null when none runs.</summary>
    public string? BatchId { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    /// <summary>The applications of the batch running now.</summary>
    public IReadOnlyList<ScreeningItem> Items { get; private set; } = [];

    /// <summary>The digest of the opening's requirements the batch running now matches against.</summary>
    public string? RequirementsHash { get; private set; }

    /// <summary>The estimated cost of the batch running now, in USD.</summary>
    public decimal? EstimatedCostUsd { get; private set; }

    /// <summary>Why it was held, as an API error code (e.g. <c>common.errors.ai_budget_exceeded</c>).</summary>
    public string? HoldReason { get; private set; }

    /// <summary>Applications analyzed so far, over every night.</summary>
    public int SucceededCount { get; private set; }

    public int FailedCount { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public Guid? CancelledByUserId { get; private set; }

    /// <summary>Waiting, running or held: an opening has at most one such screening.</summary>
    public bool IsOpen => Status is ScreeningStatus.Queued or ScreeningStatus.Submitted or ScreeningStatus.Held;

    /// <summary>HR asks to screen an opening's applications; the night run sends them.</summary>
    public static OpeningScreening Request(JobOpening opening, Guid userId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(opening);
        return new OpeningScreening
        {
            Id = EntityId.New<OpeningScreeningId>(),
            OpeningId = opening.Id,
            CompanyId = opening.CompanyId,
            Status = ScreeningStatus.Queued,
            RequestedByUserId = userId,
            RequestedAt = at,
        };
    }

    /// <summary>The night run sent a batch.</summary>
    public Result Submit(string batchId, IReadOnlyList<ScreeningItem> items, string requirementsHash, decimal estimatedCostUsd, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (Status is not (ScreeningStatus.Queued or ScreeningStatus.Held))
        {
            return OpeningScreeningErrors.NotWaiting;
        }

        if (items.Count == 0)
        {
            throw new ArgumentException("A batch has applications.", nameof(items));
        }

        BatchId = Text.Required(batchId, BatchIdMaxLength, nameof(batchId));
        Items = [.. items];
        RequirementsHash = requirementsHash.Length == RequirementsHashLength ? requirementsHash : throw new ArgumentException("The digest has 64 hex digits.", nameof(requirementsHash));
        EstimatedCostUsd = estimatedCostUsd;
        SubmittedAt = at;
        HoldReason = null;
        Status = ScreeningStatus.Submitted;
        return Result.Success();
    }

    /// <summary>The night run could not send it; the next night tries again.</summary>
    public Result Hold(string reason)
    {
        if (Status is not (ScreeningStatus.Queued or ScreeningStatus.Held))
        {
            return OpeningScreeningErrors.NotWaiting;
        }

        HoldReason = Text.Required(reason, ReasonMaxLength, nameof(reason));
        Status = ScreeningStatus.Held;
        return Result.Success();
    }

    /// <summary>
    /// The results of the running batch were stored. With applications still to analyze (left out by the night's limit),
    /// the screening waits for the next night; otherwise it is done.
    /// </summary>
    public Result Collect(int succeeded, int failed, bool moreToDo, DateTimeOffset at)
    {
        if (Status != ScreeningStatus.Submitted)
        {
            return OpeningScreeningErrors.NotSubmitted;
        }

        SucceededCount += succeeded;
        FailedCount += failed;
        BatchId = null;
        Items = [];
        RequirementsHash = null;
        Status = moreToDo ? ScreeningStatus.Queued : ScreeningStatus.Completed;
        CompletedAt = moreToDo ? null : at;
        return Result.Success();
    }

    /// <summary>Nothing (more) to analyze: the night run closes it without a batch.</summary>
    public Result Finish(DateTimeOffset at)
    {
        if (Status is not (ScreeningStatus.Queued or ScreeningStatus.Held))
        {
            return OpeningScreeningErrors.NotWaiting;
        }

        HoldReason = null;
        Status = ScreeningStatus.Completed;
        CompletedAt = at;
        return Result.Success();
    }

    /// <summary>HR calls off a screening not sent yet; a running batch is collected as usual.</summary>
    public Result Cancel(Guid userId, DateTimeOffset at)
    {
        if (Status is not (ScreeningStatus.Queued or ScreeningStatus.Held))
        {
            return OpeningScreeningErrors.NotCancellable;
        }

        Status = ScreeningStatus.Cancelled;
        CancelledAt = at;
        CancelledByUserId = userId;
        return Result.Success();
    }
}

public static class OpeningScreeningErrors
{
    public static readonly Error AlreadyOpen = Error.Conflict("recruitment.screenings.errors.already_open");
    public static readonly Error NoRequirements = Error.Validation([new FieldError("qualifications", "recruitment.screenings.errors.no_requirements")]);
    public static readonly Error NotWaiting = Error.Conflict("recruitment.screenings.errors.not_waiting");
    public static readonly Error NotSubmitted = Error.Conflict("recruitment.screenings.errors.not_submitted");
    public static readonly Error NotCancellable = Error.Conflict("recruitment.screenings.errors.not_cancellable");
}
