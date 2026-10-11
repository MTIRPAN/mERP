using System.Text.Json.Serialization;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Platform.Domain.Approvals;

public readonly record struct ApprovalRequestId(Guid Value) : IEntityId<ApprovalRequestId>
{
    public static ApprovalRequestId From(Guid value) => new(value);
}

public readonly record struct ApprovalTaskId(Guid Value) : IEntityId<ApprovalTaskId>
{
    public static ApprovalTaskId From(Guid value) => new(value);
}

/// <summary>States of a request (§2.6).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ApprovalStatus>))]
public enum ApprovalStatus
{
    Pending,
    Approved,
    Rejected,
    Returned,
    Cancelled,
    NeedsAttention,
}

[JsonConverter(typeof(JsonStringEnumConverter<ApprovalTaskStatus>))]
public enum ApprovalTaskStatus
{
    Pending,
    Approved,
    Rejected,
    Returned,

    /// <summary>Not needed any more: another approver decided, the request ended or was cancelled.</summary>
    Closed,

    /// <summary>Moved to another approver by an administrator or an escalation.</summary>
    Reassigned,
}

[JsonConverter(typeof(JsonStringEnumConverter<ApprovalDecision>))]
public enum ApprovalDecision
{
    Approve,
    Reject,
    Return,
}

/// <summary>Kinds of entries of a request's timeline (§2.6).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ApprovalEventKind>))]
public enum ApprovalEventKind
{
    Submitted,
    Resubmitted,
    Assigned,
    Skipped,
    Approved,
    Rejected,
    Returned,
    Cancelled,
    NeedsAttention,
    Reassigned,
    Escalated,
    Reminded,
    ForceApproved,
    Completed,
}

/// <summary>What a decision did to the request.</summary>
public enum DecisionEffect
{
    /// <summary>The step waits for other approvers (All mode).</summary>
    StepPending,

    /// <summary>The step is complete; the flow moves on.</summary>
    StepCompleted,

    Rejected,
    Returned,
}

/// <summary>
/// A document under approval (§2.1): the facts and steps copied at submission, the current step, the tasks of the
/// approvers and the timeline. One request per document; a returned document is resubmitted on the same request
/// with a new round, so earlier decisions stay in the timeline (scenario 7).
/// </summary>
[Audited]
public sealed class ApprovalRequest : AuditableAggregateRoot<ApprovalRequestId>
{
    public const int DocumentTypeMaxLength = 100;
    public const int DocumentIdMaxLength = 100;
    public const int CommentMaxLength = 2000;

    public const string CommentRequiredCode = "platform.approvals.errors.comment_required";

    /// <summary>Timeline detail of a system decision on an overdue step.</summary>
    public const string OverdueReason = "overdue";
    public static readonly Error TaskNotOpen = Error.Conflict("platform.approvals.errors.task_not_open");
    public static readonly Error ReturnNotAllowed = Error.Conflict("platform.approvals.errors.return_not_allowed");
    public static readonly Error NotPending = Error.Conflict("platform.approvals.errors.not_pending");

    private readonly List<ApprovalTask> _tasks = [];
    private readonly List<ApprovalHistoryEntry> _history = [];

    private ApprovalRequest()
    {
    }

    public string DocumentType { get; private set; } = null!;

    public string DocumentId { get; private set; } = null!;

    public Guid? CompanyId { get; private set; }

    public Guid RequesterUserId { get; private set; }

    public Guid? RequesterEmployeeId { get; private set; }

    /// <summary>The document's facts as JSON, copied at (re)submission.</summary>
    public string Facts { get; private set; } = "{}";

    public string TitleKey { get; private set; } = null!;

    public IReadOnlyDictionary<string, string> TitleArgs { get; private set; } = new Dictionary<string, string>();

    public string? Summary { get; private set; }

    public string? Link { get; private set; }

    /// <summary>The definition the steps were copied from; null when none matched.</summary>
    public Guid? DefinitionId { get; private set; }

    public int? DefinitionRevision { get; private set; }

    public IReadOnlyList<ApprovalStep> Steps { get; private set; } = [];

    public ApproverRule? Fallback { get; private set; }

    public ApprovalStatus Status { get; private set; }

    /// <summary>1 at submission, one more after each resubmission.</summary>
    public int Round { get; private set; }

    /// <summary>Index of the step being worked on; null before the first step of a round.</summary>
    public int? CurrentStep { get; private set; }

    public DateTimeOffset SubmittedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>
    /// When the timeline last grew. Every change of the request or its tasks sets it, so the request row changes and
    /// two concurrent decisions conflict on its version (§2.6) even when only tasks change.
    /// </summary>
    public DateTimeOffset LastActivityAt { get; private set; }

    public IReadOnlyCollection<ApprovalTask> Tasks => _tasks.AsReadOnly();

    public IReadOnlyCollection<ApprovalHistoryEntry> History => _history.AsReadOnly();

    public ApprovalStep? Step => CurrentStep is { } index ? Steps[index] : null;

    public IEnumerable<ApprovalTask> OpenTasks => _tasks.Where(t => t.Status == ApprovalTaskStatus.Pending);

    public static ApprovalRequest Submit(
        string documentType,
        string documentId,
        Guid? companyId,
        Guid requesterUserId,
        Guid? requesterEmployeeId,
        ApprovalContent content,
        ApprovalDefinition? definition,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(content);
        var request = new ApprovalRequest
        {
            Id = EntityId.New<ApprovalRequestId>(),
            DocumentType = Text.Required(documentType, DocumentTypeMaxLength, nameof(documentType)),
            DocumentId = Text.Required(documentId, DocumentIdMaxLength, nameof(documentId)),
            CompanyId = companyId,
            RequesterUserId = requesterUserId,
            RequesterEmployeeId = requesterEmployeeId,
            DefinitionId = definition?.Id.Value,
            DefinitionRevision = definition?.Revision,
            Steps = definition?.Steps ?? [],
            Fallback = definition?.Fallback,
            Status = ApprovalStatus.Pending,
            Round = 1,
            SubmittedAt = now,
        };
        request.Apply(content);
        request.Record(ApprovalEventKind.Submitted, now, userId: requesterUserId);
        return request;
    }

    /// <summary>A returned document comes back: a new round starts at the first step with the new facts (§2.5).</summary>
    public Result Resubmit(ApprovalContent content, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (Status != ApprovalStatus.Returned)
        {
            return NotPending;
        }

        Apply(content);
        Round++;
        CurrentStep = null;
        Status = ApprovalStatus.Pending;
        CompletedAt = null;
        Record(ApprovalEventKind.Resubmitted, now, userId: RequesterUserId);
        return Result.Success();
    }

    /// <summary>Index of the step after the current one; null when no step is left.</summary>
    public int? NextStep => (CurrentStep ?? -1) + 1 < Steps.Count ? (CurrentStep ?? -1) + 1 : null;

    /// <summary>The step does not apply (condition, requester or earlier approver, §2.5); the timeline says why.</summary>
    public void SkipStep(int step, string reason, DateTimeOffset now)
    {
        EnsureWorking();
        CurrentStep = step;
        Status = ApprovalStatus.Pending;
        Record(ApprovalEventKind.Skipped, now, step: step, detail: reason);
    }

    /// <summary>Gives the approvers of a step their tasks.</summary>
    public IReadOnlyList<ApprovalTask> OpenStep(int step, IReadOnlyCollection<Guid> approvers, DateTimeOffset? dueAt, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(approvers);
        if (approvers.Count == 0)
        {
            throw new ArgumentException("A step needs at least one approver.", nameof(approvers));
        }

        EnsureWorking();
        CurrentStep = step;
        Status = ApprovalStatus.Pending;
        var tasks = approvers.Distinct().Select(user => new ApprovalTask(Id, Round, step, user, now, dueAt)).ToList();
        _tasks.AddRange(tasks);
        foreach (var task in tasks)
        {
            Record(ApprovalEventKind.Assigned, now, step: step, userId: task.AssigneeUserId);
        }

        return tasks;
    }

    /// <summary>
    /// Nobody could be found for the step and there is no fallback (§2.4), or no definition matched (step null, §2.2):
    /// an administrator must assign approvers or approve.
    /// </summary>
    public void NeedAttention(int? step, string reason, DateTimeOffset now)
    {
        EnsureWorking();
        CurrentStep = step;
        Status = ApprovalStatus.NeedsAttention;
        Record(ApprovalEventKind.NeedsAttention, now, step: step, detail: reason);
    }

    /// <summary>Every step is done (or none applies): approved.</summary>
    public void Approve(DateTimeOffset now, string? reason = null)
    {
        EnsureWorking();
        Status = ApprovalStatus.Approved;
        CompletedAt = now;
        Record(ApprovalEventKind.Completed, now, detail: reason ?? nameof(ApprovalStatus.Approved));
    }

    /// <summary>
    /// A decision on a task, by its assignee or a delegate (the task then records the assignee as "on behalf of"). Reject
    /// and return need a comment; return only where the step allows it (§2.5, §2.6).
    /// </summary>
    public Result<DecisionEffect> Decide(ApprovalTaskId taskId, Guid decidedBy, ApprovalDecision decision, string? comment, DateTimeOffset now)
    {
        var task = _tasks.Find(t => t.Id == taskId);
        if (task is not { Status: ApprovalTaskStatus.Pending } || Status != ApprovalStatus.Pending || task.Round != Round || task.Step != CurrentStep)
        {
            return TaskNotOpen;
        }

        var text = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (decision != ApprovalDecision.Approve && text is null)
        {
            return Error.Validation([new FieldError("comment", CommentRequiredCode)]);
        }

        if (text is { Length: > CommentMaxLength })
        {
            return Error.Validation([new FieldError("comment", "common.errors.max_length", new Dictionary<string, object?> { ["maxLength"] = CommentMaxLength })]);
        }

        if (decision == ApprovalDecision.Return && Step is not { AllowReturn: true })
        {
            return ReturnNotAllowed;
        }

        Guid? onBehalfOf = decidedBy == task.AssigneeUserId ? null : task.AssigneeUserId;
        switch (decision)
        {
            case ApprovalDecision.Approve:
                task.Decide(ApprovalTaskStatus.Approved, decidedBy, onBehalfOf, text, now);
                Record(ApprovalEventKind.Approved, now, step: task.Step, userId: decidedBy, onBehalfOf: onBehalfOf, detail: text);
                if (Step!.Mode == ApprovalMode.Any || CurrentTasks().All(t => t.Status == ApprovalTaskStatus.Approved))
                {
                    CloseOpenTasks(now);
                    return DecisionEffect.StepCompleted;
                }

                return DecisionEffect.StepPending;

            case ApprovalDecision.Reject:
                task.Decide(ApprovalTaskStatus.Rejected, decidedBy, onBehalfOf, text, now);
                Record(ApprovalEventKind.Rejected, now, step: task.Step, userId: decidedBy, onBehalfOf: onBehalfOf, detail: text);
                CloseOpenTasks(now);
                Status = ApprovalStatus.Rejected;
                CompletedAt = now;
                return DecisionEffect.Rejected;

            default:
                task.Decide(ApprovalTaskStatus.Returned, decidedBy, onBehalfOf, text, now);
                Record(ApprovalEventKind.Returned, now, step: task.Step, userId: decidedBy, onBehalfOf: onBehalfOf, detail: text);
                CloseOpenTasks(now);
                Status = ApprovalStatus.Returned;
                return DecisionEffect.Returned;
        }
    }

    /// <summary>
    /// An overdue step decided by the system (§2.5 <c>auto-approve</c> / <c>auto-reject</c>): the open tasks close and
    /// the timeline shows no user.
    /// </summary>
    public Result<DecisionEffect> DecideOverdue(ApprovalDecision decision, DateTimeOffset now)
    {
        if (Status != ApprovalStatus.Pending || CurrentStep is null || decision == ApprovalDecision.Return)
        {
            return NotPending;
        }

        CloseOpenTasks(now);
        if (decision == ApprovalDecision.Approve)
        {
            Record(ApprovalEventKind.Approved, now, step: CurrentStep, detail: OverdueReason);
            return DecisionEffect.StepCompleted;
        }

        Record(ApprovalEventKind.Rejected, now, step: CurrentStep, detail: OverdueReason);
        Status = ApprovalStatus.Rejected;
        CompletedAt = now;
        return DecisionEffect.Rejected;
    }

    /// <summary>
    /// An administrator approves the current step in place of its approvers (§2.6). A request that needs attention
    /// may be approved too, also when no definition matched; the flow then moves on.
    /// </summary>
    public Result ForceApprove(Guid administrator, string? comment, DateTimeOffset now)
    {
        if (Status is not (ApprovalStatus.Pending or ApprovalStatus.NeedsAttention))
        {
            return NotPending;
        }

        CloseOpenTasks(now);
        Record(ApprovalEventKind.ForceApproved, now, step: CurrentStep, userId: administrator, detail: string.IsNullOrWhiteSpace(comment) ? null : comment.Trim());
        return Result.Success();
    }

    /// <summary>Moves an open task to another approver: an administrator's reassignment or an escalation (§2.5, §2.6).</summary>
    public Result<ApprovalTask> Reassign(ApprovalTaskId taskId, Guid newAssignee, Guid? actor, bool escalation, DateTimeOffset now)
    {
        var task = _tasks.Find(t => t.Id == taskId);
        if (task is not { Status: ApprovalTaskStatus.Pending } || Status != ApprovalStatus.Pending)
        {
            return TaskNotOpen;
        }

        task.Reassign(now);
        var moved = new ApprovalTask(Id, Round, task.Step, newAssignee, now, task.DueAt is { } due ? now + (due - task.CreatedAt) : null);
        _tasks.Add(moved);
        Record(escalation ? ApprovalEventKind.Escalated : ApprovalEventKind.Reassigned, now, step: task.Step, userId: actor, onBehalfOf: task.AssigneeUserId, detail: newAssignee.ToString());
        Record(ApprovalEventKind.Assigned, now, step: task.Step, userId: newAssignee);
        return moved;
    }

    /// <summary>The approvers of an overdue task were reminded; it is not reminded again.</summary>
    public void Remind(ApprovalTaskId taskId, DateTimeOffset now)
    {
        var task = _tasks.Find(t => t.Id == taskId) ?? throw new ArgumentException("Unknown task.", nameof(taskId));
        task.MarkEscalated(now);
        Record(ApprovalEventKind.Reminded, now, step: task.Step, userId: task.AssigneeUserId);
    }

    /// <summary>The module withdrew the document (§2.7): open tasks close.</summary>
    public Result Cancel(DateTimeOffset now)
    {
        if (Status is ApprovalStatus.Approved or ApprovalStatus.Rejected or ApprovalStatus.Cancelled)
        {
            return NotPending;
        }

        CloseOpenTasks(now);
        Status = ApprovalStatus.Cancelled;
        CompletedAt = now;
        Record(ApprovalEventKind.Cancelled, now, userId: RequesterUserId);
        return Result.Success();
    }

    /// <summary>Users who approved in the current round, for <see cref="ApprovalStep.SkipIfAlreadyApproved"/>.</summary>
    public IReadOnlySet<Guid> ApprovedInRound() =>
        _tasks.Where(t => t.Round == Round && t.Status == ApprovalTaskStatus.Approved)
            .Select(t => t.AssigneeUserId)
            .ToHashSet();

    private IEnumerable<ApprovalTask> CurrentTasks() =>
        _tasks.Where(t => t.Round == Round && t.Step == CurrentStep && t.Status != ApprovalTaskStatus.Reassigned);

    private void CloseOpenTasks(DateTimeOffset now)
    {
        foreach (var open in _tasks.Where(t => t.Status == ApprovalTaskStatus.Pending))
        {
            open.Close(now);
        }
    }

    private void Apply(ApprovalContent content)
    {
        Facts = content.Facts;
        TitleKey = content.TitleKey;
        TitleArgs = content.TitleArgs;
        Summary = content.Summary;
        Link = content.Link;
    }

    private void EnsureWorking()
    {
        if (Status is not (ApprovalStatus.Pending or ApprovalStatus.NeedsAttention))
        {
            throw new InvalidOperationException($"A {Status} request has no steps to work on.");
        }
    }

    private void Record(ApprovalEventKind kind, DateTimeOffset now, int? step = null, Guid? userId = null, Guid? onBehalfOf = null, string? detail = null)
    {
        _history.Add(new ApprovalHistoryEntry(Id, _history.Count + 1, kind, Round, step, userId, onBehalfOf, detail, now));
        LastActivityAt = now;
    }
}

/// <summary>What a submission carries besides the requester: facts and the texts of the inbox.</summary>
public sealed record ApprovalContent(string Facts, string TitleKey, IReadOnlyDictionary<string, string> TitleArgs, string? Summary, string? Link);

/// <summary>An approver's task in a step of a round (§2.1).</summary>
[Audited]
public sealed class ApprovalTask : Entity<ApprovalTaskId>
{
    private ApprovalTask()
    {
    }

    internal ApprovalTask(ApprovalRequestId requestId, int round, int step, Guid assigneeUserId, DateTimeOffset now, DateTimeOffset? dueAt)
        : base(EntityId.New<ApprovalTaskId>())
    {
        RequestId = requestId;
        Round = round;
        Step = step;
        AssigneeUserId = assigneeUserId;
        Status = ApprovalTaskStatus.Pending;
        CreatedAt = now;
        DueAt = dueAt;
    }

    public ApprovalRequestId RequestId { get; private set; }

    public int Round { get; private set; }

    public int Step { get; private set; }

    public Guid AssigneeUserId { get; private set; }

    public ApprovalTaskStatus Status { get; private set; }

    /// <summary>Who decided: the assignee or their delegate.</summary>
    public Guid? DecidedByUserId { get; private set; }

    /// <summary>Set when a delegate decided: the assignee they acted for ("X adına Y", §2.6).</summary>
    public Guid? OnBehalfOfUserId { get; private set; }

    public string? Comment { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? DueAt { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>When the overdue task was escalated or its approver reminded; it is handled once.</summary>
    public DateTimeOffset? EscalatedAt { get; private set; }

    internal void Decide(ApprovalTaskStatus status, Guid decidedBy, Guid? onBehalfOf, string? comment, DateTimeOffset now)
    {
        Status = status;
        DecidedByUserId = decidedBy;
        OnBehalfOfUserId = onBehalfOf;
        Comment = comment;
        DecidedAt = now;
    }

    internal void Close(DateTimeOffset now)
    {
        Status = ApprovalTaskStatus.Closed;
        DecidedAt = now;
    }

    internal void Reassign(DateTimeOffset now)
    {
        Status = ApprovalTaskStatus.Reassigned;
        DecidedAt = now;
        EscalatedAt ??= now;
    }

    internal void MarkEscalated(DateTimeOffset now) => EscalatedAt = now;
}

/// <summary>An entry of a request's timeline; <see cref="Sequence"/> keeps the order of entries of the same moment.</summary>
public sealed class ApprovalHistoryEntry
{
    private ApprovalHistoryEntry()
    {
    }

    internal ApprovalHistoryEntry(
        ApprovalRequestId requestId,
        int sequence,
        ApprovalEventKind kind,
        int round,
        int? step,
        Guid? userId,
        Guid? onBehalfOfUserId,
        string? detail,
        DateTimeOffset at)
    {
        Id = Guid.CreateVersion7();
        RequestId = requestId;
        Sequence = sequence;
        Kind = kind;
        Round = round;
        Step = step;
        UserId = userId;
        OnBehalfOfUserId = onBehalfOfUserId;
        Detail = detail;
        At = at;
    }

    /// <summary>Own key, so two concurrent decisions conflict on the request version, not on the timeline.</summary>
    public Guid Id { get; private set; }

    public ApprovalRequestId RequestId { get; private set; }

    public int Sequence { get; private set; }

    public ApprovalEventKind Kind { get; private set; }

    public int Round { get; private set; }

    public int? Step { get; private set; }

    /// <summary>Who acted; null for the system.</summary>
    public Guid? UserId { get; private set; }

    public Guid? OnBehalfOfUserId { get; private set; }

    /// <summary>Comment, skip or completion reason, or the new assignee of a reassignment.</summary>
    public string? Detail { get; private set; }

    public DateTimeOffset At { get; private set; }
}
