using System.Text.Json.Serialization;
using Merp.Modules.HR.Domain.Employees;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;

namespace Merp.Modules.HR.Domain.Leave;

public readonly record struct LeaveRequestId(Guid Value) : IEntityId<LeaveRequestId>
{
    public static LeaveRequestId From(Guid value) => new(value);
}

/// <summary>Which part of a day a request covers on its first or last day (docs/domain/izin-kurallari.md §4).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LeaveDayPart>))]
public enum LeaveDayPart
{
    Full,

    /// <summary>Only the morning: the last day of a request ending at noon.</summary>
    Morning,

    /// <summary>Only the afternoon: the first day of a request starting at noon.</summary>
    Afternoon,
}

/// <summary>Where a leave request stands (D44).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LeaveRequestStatus>))]
public enum LeaveRequestStatus
{
    /// <summary>Saved, not sent yet.</summary>
    Draft,

    /// <summary>Waits for approval.</summary>
    Pending,

    /// <summary>An approver sent it back for correction; the requester corrects and sends it again, or cancels it.</summary>
    Returned,

    Approved,

    Rejected,

    Cancelled,

    /// <summary>Approved; the cancellation the requester asked for waits for approval.</summary>
    CancellationPending,
}

/// <summary>How a leave request came in (D44).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LeaveRequestOrigin>))]
public enum LeaveRequestOrigin
{
    /// <summary>The employee asked for it.</summary>
    Self,

    /// <summary>HR asked for it on the employee's behalf.</summary>
    OnBehalf,

    /// <summary>HR recorded it approved, without a flow (e.g. a past leave).</summary>
    Record,

    /// <summary>HR recorded it approved with a bulk leave.</summary>
    Bulk,
}

/// <summary>
/// What a request asks for and how long it is, as the application checked and counted it (D44).
/// </summary>
/// <param name="LeaveTypeId">The leave type.</param>
/// <param name="LeaveTypeCode">Its code.</param>
/// <param name="DeductsFrom">The code of the balance it uses (YILLIK); null when it uses none.</param>
/// <param name="StartDate">First day.</param>
/// <param name="EndDate">Last day of leave, without the travel days.</param>
/// <param name="StartPart">Full, or Afternoon for a leave starting at noon.</param>
/// <param name="EndPart">Full, or Morning for a leave ending at noon.</param>
/// <param name="StartTime">Start of an hourly leave.</param>
/// <param name="EndTime">End of an hourly leave.</param>
/// <param name="EventDate">The event of an event based leave (the birth).</param>
/// <param name="TravelDays">Calendar days of travel after the last day (yol izni), not taken from the balance.</param>
/// <param name="Basis">How the length was counted.</param>
/// <param name="Quantity">Days, or hours for an hourly leave.</param>
/// <param name="Days">The length in days; the hours as days for an hourly leave.</param>
public sealed record LeaveRequestPeriod(
    LeaveTypeId LeaveTypeId,
    string LeaveTypeCode,
    string? DeductsFrom,
    DateOnly StartDate,
    DateOnly EndDate,
    LeaveDayPart StartPart,
    LeaveDayPart EndPart,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    DateOnly? EventDate,
    int TravelDays,
    DurationBasis Basis,
    decimal Quantity,
    decimal Days)
{
    /// <summary>The last day away, travel days included.</summary>
    public DateOnly AwayUntil => EndDate.AddDays(TravelDays);

    /// <summary>
    /// Whether the two leaves share time: their days meet, unless one ends in the morning of the day the other starts
    /// in the afternoon, or both are hourly on the same single day with separate hours.
    /// </summary>
    public bool Overlaps(LeaveRequestPeriod other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (StartDate > other.AwayUntil || other.StartDate > AwayUntil)
        {
            return false;
        }

        if (StartTime is { } start && EndTime is { } end && other.StartTime is { } otherStart && other.EndTime is { } otherEnd
            && StartDate == EndDate && other.StartDate == other.EndDate && StartDate == other.StartDate)
        {
            return start < otherEnd && otherStart < end;
        }

        return !Touches(this, other) && !Touches(other, this);
    }

    /// <summary>The first ends in the morning of the only shared day, on which the second starts in the afternoon.</summary>
    private static bool Touches(LeaveRequestPeriod first, LeaveRequestPeriod second) =>
        first.TravelDays == 0 && first.EndDate == second.StartDate && first.EndPart == LeaveDayPart.Morning && second.StartPart == LeaveDayPart.Afternoon;
}

/// <summary>The requester's notes: the explanation, where to reach them and who stands in for them.</summary>
/// <param name="Note">Açıklama.</param>
/// <param name="Address">Address during the leave.</param>
/// <param name="Phone">Phone during the leave.</param>
/// <param name="SubstituteEmployeeId">The colleague who stands in (vekil).</param>
public sealed record LeaveRequestContact(string? Note, string? Address, string? Phone, EmployeeId? SubstituteEmployeeId);

/// <summary>
/// A leave of an employment, asked for and approved through the approval engine or recorded by HR (PLAN L4, D44).
/// Approval writes its days to the balance and cancellation gives them back, both by domain events in the same
/// transaction (CLAUDE.md rule 7).
/// </summary>
[Audited]
public sealed class LeaveRequest : AuditableAggregateRoot<LeaveRequestId>, ICompanyScoped
{
    public const int NumberMaxLength = 30;
    public const int NoteMaxLength = 500;
    public const int AddressMaxLength = 300;
    public const int PhoneMaxLength = 30;
    public const int CommentMaxLength = 500;
    public const decimal TenDayBlock = 10;

    private LeaveRequest()
    {
    }

    /// <summary>P5 <c>hr.leave-request</c>, e.g. IZN-2026-000012.</summary>
    public string Number { get; private set; } = null!;

    public EmployeeId EmployeeId { get; private set; }

    public EmploymentId EmploymentId { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>The user who asked: the employee, or HR for a request on their behalf.</summary>
    public Guid RequesterUserId { get; private set; }

    public LeaveRequestOrigin Origin { get; private set; }

    public LeaveRequestStatus Status { get; private set; }

    public LeaveTypeId LeaveTypeId { get; private set; }

    public string LeaveTypeCode { get; private set; } = null!;

    /// <summary>The code of the balance it uses (YILLIK); null when it uses none.</summary>
    public string? DeductsFrom { get; private set; }

    public DateOnly StartDate { get; private set; }

    /// <summary>Last day of leave, without the travel days.</summary>
    public DateOnly EndDate { get; private set; }

    public LeaveDayPart StartPart { get; private set; }

    public LeaveDayPart EndPart { get; private set; }

    public TimeOnly? StartTime { get; private set; }

    public TimeOnly? EndTime { get; private set; }

    /// <summary>The event of an event based leave (the birth).</summary>
    public DateOnly? EventDate { get; private set; }

    /// <summary>Calendar days of travel after the last day (yol izni).</summary>
    public int TravelDays { get; private set; }

    public DurationBasis Basis { get; private set; }

    /// <summary>Days, or hours for an hourly leave.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>The length in days, taken from the balance when it uses one.</summary>
    public decimal Days { get; private set; }

    public string? Note { get; private set; }

    public string? Address { get; private set; }

    public string? Phone { get; private set; }

    public EmployeeId? SubstituteEmployeeId { get; private set; }

    /// <summary>The leave as one value.</summary>
    public LeaveRequestPeriod Period =>
        new(LeaveTypeId, LeaveTypeCode, DeductsFrom, StartDate, EndDate, StartPart, EndPart, StartTime, EndTime, EventDate, TravelDays, Basis, Quantity, Days);

    /// <summary>Why an approver returned it.</summary>
    public string? ReturnComment { get; private set; }

    /// <summary>Why it was cancelled, or why its cancellation was asked for.</summary>
    public string? CancellationReason { get; private set; }

    /// <summary>When its cancellation was asked for; a request asks once (D44).</summary>
    public DateTimeOffset? CancellationRequestedAt { get; private set; }

    /// <summary>The last day before an early return (erken dönüş).</summary>
    public DateOnly? OriginalEndDate { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    /// <summary>When it was approved, rejected or cancelled.</summary>
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>When the employee and the manager were reminded the day before.</summary>
    public DateTimeOffset? RemindedAt { get; private set; }

    /// <summary>Waits for approval or correction: its days are reserved on the balance (§5).</summary>
    public bool IsOpen => Status is LeaveRequestStatus.Pending or LeaveRequestStatus.Returned;

    /// <summary>Days off: approved, or asked for and not refused.</summary>
    public bool Blocks => Status is LeaveRequestStatus.Pending or LeaveRequestStatus.Returned or LeaveRequestStatus.Approved or LeaveRequestStatus.CancellationPending;

    public bool IsApproved => Status is LeaveRequestStatus.Approved or LeaveRequestStatus.CancellationPending;

    /// <summary>A draft the employee or HR keeps on correcting before sending it.</summary>
    public static LeaveRequest Draft(
        string number,
        EmployeeId employeeId,
        EmploymentId employmentId,
        Guid companyId,
        Guid requesterUserId,
        LeaveRequestOrigin origin,
        LeaveRequestPeriod period,
        LeaveRequestContact contact)
    {
        if (origin is not (LeaveRequestOrigin.Self or LeaveRequestOrigin.OnBehalf))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), origin, "Only asked requests start as drafts.");
        }

        var request = new LeaveRequest
        {
            Id = EntityId.New<LeaveRequestId>(),
            Number = Text.Required(number, NumberMaxLength, nameof(number)),
            EmployeeId = employeeId,
            EmploymentId = employmentId,
            CompanyId = companyId,
            RequesterUserId = requesterUserId,
            Origin = origin,
            Status = LeaveRequestStatus.Draft,
        };
        request.Set(period, contact);
        return request;
    }

    /// <summary>HR records a leave approved, without a flow (a past leave or a bulk leave).</summary>
    public static LeaveRequest Record(
        string number,
        EmployeeId employeeId,
        EmploymentId employmentId,
        Guid companyId,
        Guid requesterUserId,
        LeaveRequestOrigin origin,
        LeaveRequestPeriod period,
        LeaveRequestContact contact,
        DateTimeOffset at)
    {
        if (origin is not (LeaveRequestOrigin.Record or LeaveRequestOrigin.Bulk))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), origin, "Only records and bulk leaves are recorded approved.");
        }

        var request = new LeaveRequest
        {
            Id = EntityId.New<LeaveRequestId>(),
            Number = Text.Required(number, NumberMaxLength, nameof(number)),
            EmployeeId = employeeId,
            EmploymentId = employmentId,
            CompanyId = companyId,
            RequesterUserId = requesterUserId,
            Origin = origin,
            SubmittedAt = at,
        };
        request.Set(period, contact);
        request.MarkApproved(at);
        return request;
    }

    /// <summary>Corrects a draft or a returned request.</summary>
    public Result Update(LeaveRequestPeriod period, LeaveRequestContact contact)
    {
        if (Status is not (LeaveRequestStatus.Draft or LeaveRequestStatus.Returned))
        {
            return LeaveRequestErrors.NotEditable;
        }

        Set(period, contact);
        return Result.Success();
    }

    /// <summary>
    /// Sends a draft or a returned request: it waits for approval, or is approved at once for a type that needs none.
    /// Returns whether the approval engine has to decide.
    /// </summary>
    public Result<bool> Submit(bool requiresApproval, DateTimeOffset at)
    {
        if (Status is not (LeaveRequestStatus.Draft or LeaveRequestStatus.Returned))
        {
            return LeaveRequestErrors.NotEditable;
        }

        SubmittedAt = at;
        ReturnComment = null;
        if (!requiresApproval)
        {
            MarkApproved(at);
            return false;
        }

        Status = LeaveRequestStatus.Pending;
        return true;
    }

    /// <summary>The flow approved it (<c>ApprovalRequestCompleted</c>); ignored unless it waits for approval.</summary>
    public void Approve(DateTimeOffset at)
    {
        if (Status == LeaveRequestStatus.Pending)
        {
            MarkApproved(at);
        }
    }

    /// <summary>The flow rejected it; ignored unless it waits for approval.</summary>
    public void Reject(DateTimeOffset at)
    {
        if (Status == LeaveRequestStatus.Pending)
        {
            Status = LeaveRequestStatus.Rejected;
            DecidedAt = at;
        }
    }

    /// <summary>An approver returned it for correction (<c>ApprovalRequestReturned</c>); ignored unless it waits.</summary>
    public void Return(string comment)
    {
        if (Status == LeaveRequestStatus.Pending)
        {
            Status = LeaveRequestStatus.Returned;
            ReturnComment = Text.Optional(comment, CommentMaxLength, nameof(comment));
        }
    }

    /// <summary>
    /// The requester (or HR) withdraws a draft or a request not decided yet. Returns whether an approval was open and
    /// has to be closed.
    /// </summary>
    public Result<bool> Cancel(string? reason, DateTimeOffset at)
    {
        if (Status is not (LeaveRequestStatus.Draft or LeaveRequestStatus.Pending or LeaveRequestStatus.Returned))
        {
            return LeaveRequestErrors.NotCancellable;
        }

        var hadApproval = Status != LeaveRequestStatus.Draft;
        Status = LeaveRequestStatus.Cancelled;
        CancellationReason = Text.Optional(reason, CommentMaxLength, nameof(reason));
        DecidedAt = at;
        return hadApproval;
    }

    /// <summary>The requester asks to cancel an approved leave that has not started; once per request (D44).</summary>
    public Result RequestCancellation(string? reason, DateOnly today, DateTimeOffset at)
    {
        if (Status != LeaveRequestStatus.Approved)
        {
            return LeaveRequestErrors.NotApproved;
        }

        if (StartDate <= today)
        {
            return LeaveRequestErrors.AlreadyStarted;
        }

        if (CancellationRequestedAt is not null)
        {
            return LeaveRequestErrors.CancellationAlreadyRequested;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return LeaveRequestErrors.ReasonRequired;
        }

        Status = LeaveRequestStatus.CancellationPending;
        CancellationReason = Text.Optional(reason, CommentMaxLength, nameof(reason));
        CancellationRequestedAt = at;
        return Result.Success();
    }

    /// <summary>The cancellation flow ended: approved, the leave is cancelled and its days come back; rejected, it stays.</summary>
    public void CompleteCancellation(bool approved, DateTimeOffset at)
    {
        if (Status != LeaveRequestStatus.CancellationPending)
        {
            return;
        }

        if (approved)
        {
            MarkCancelled(at);
        }
        else
        {
            Status = LeaveRequestStatus.Approved;
        }
    }

    /// <summary>
    /// HR cancels an approved leave that has not started, without a flow. Returns whether a cancellation flow was open
    /// and has to be closed.
    /// </summary>
    public Result<bool> CancelApproved(string? reason, DateOnly today, DateTimeOffset at)
    {
        if (!IsApproved)
        {
            return LeaveRequestErrors.NotApproved;
        }

        if (StartDate <= today)
        {
            return LeaveRequestErrors.AlreadyStarted;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return LeaveRequestErrors.ReasonRequired;
        }

        var flowOpen = Status == LeaveRequestStatus.CancellationPending;
        CancellationReason = Text.Optional(reason, CommentMaxLength, nameof(reason));
        MarkCancelled(at);
        return flowOpen;
    }

    /// <summary>
    /// HR ends a started leave early (erken dönüş): <paramref name="period"/> is the leave counted again up to its new
    /// last day. The days no longer taken come back to the balance.
    /// </summary>
    public Result ReturnEarly(LeaveRequestPeriod period, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(period);
        if (Status != LeaveRequestStatus.Approved)
        {
            return LeaveRequestErrors.NotApproved;
        }

        if (StartDate > today)
        {
            return LeaveRequestErrors.NotStarted;
        }

        if (period.EndDate >= EndDate || period.EndDate < StartDate || period.StartDate != StartDate)
        {
            return LeaveRequestErrors.InvalidEarlyReturn;
        }

        var given = Days - period.Days;
        OriginalEndDate ??= EndDate;
        SetPeriod(period with { TravelDays = 0 });
        if (given > 0 && DeductsFrom is { } code)
        {
            Raise(new LeaveRequestDaysReturned(Id, EmploymentId, code, period.EndDate.AddDays(1), given));
        }

        return Result.Success();
    }

    /// <summary>The reminder of the day before was sent.</summary>
    public void MarkReminded(DateTimeOffset at) => RemindedAt = at;

    private void MarkApproved(DateTimeOffset at)
    {
        Status = LeaveRequestStatus.Approved;
        DecidedAt = at;
        if (DeductsFrom is { } code)
        {
            Raise(new LeaveRequestApproved(Id, EmployeeId, EmploymentId, CompanyId, code, StartDate, Days));
        }
    }

    private void MarkCancelled(DateTimeOffset at)
    {
        Status = LeaveRequestStatus.Cancelled;
        DecidedAt = at;
        if (DeductsFrom is { } code)
        {
            Raise(new LeaveRequestCancelled(Id, EmploymentId, code, StartDate));
        }
    }

    private void Set(LeaveRequestPeriod period, LeaveRequestContact contact)
    {
        ArgumentNullException.ThrowIfNull(contact);
        SetPeriod(period);
        Note = Text.Optional(contact.Note, NoteMaxLength, nameof(contact.Note));
        Address = Text.Optional(contact.Address, AddressMaxLength, nameof(contact.Address));
        Phone = Text.Optional(contact.Phone, PhoneMaxLength, nameof(contact.Phone));
        SubstituteEmployeeId = contact.SubstituteEmployeeId;
    }

    private void SetPeriod(LeaveRequestPeriod period)
    {
        ArgumentNullException.ThrowIfNull(period);
        if (period.EndDate < period.StartDate || period.Quantity <= 0 || period.Days < 0 || period.TravelDays < 0)
        {
            throw new ArgumentException("The period is not a counted leave.", nameof(period));
        }

        LeaveTypeId = period.LeaveTypeId;
        LeaveTypeCode = period.LeaveTypeCode.Trim().ToUpperInvariant();
        DeductsFrom = period.DeductsFrom?.Trim().ToUpperInvariant();
        StartDate = period.StartDate;
        EndDate = period.EndDate;
        StartPart = period.StartPart;
        EndPart = period.EndPart;
        StartTime = period.StartTime;
        EndTime = period.EndTime;
        EventDate = period.EventDate;
        TravelDays = period.TravelDays;
        Basis = period.Basis;
        Quantity = period.Quantity;
        Days = period.Days;
    }
}

public static class LeaveRequestErrors
{
    public static readonly Error NotEditable = Error.Conflict("hr.leave.errors.request_not_editable");
    public static readonly Error NotCancellable = Error.Conflict("hr.leave.errors.request_not_cancellable");
    public static readonly Error NotApproved = Error.Conflict("hr.leave.errors.request_not_approved");
    public static readonly Error AlreadyStarted = Error.Conflict("hr.leave.errors.request_started");
    public static readonly Error NotStarted = Error.Conflict("hr.leave.errors.request_not_started");
    public static readonly Error CancellationAlreadyRequested = Error.Conflict("hr.leave.errors.cancellation_already_requested");
    public static readonly Error ReasonRequired = Error.Validation([new FieldError("reason", "hr.leave.errors.reason_required")]);
    public static readonly Error InvalidEarlyReturn = Error.Validation([new FieldError("endDate", "hr.leave.errors.invalid_early_return")]);
}

/// <summary>An approved leave takes its days from the balance in the same transaction (rule 7).</summary>
public sealed record LeaveRequestApproved(
    LeaveRequestId RequestId,
    EmployeeId EmployeeId,
    EmploymentId EmploymentId,
    Guid CompanyId,
    string LeaveTypeCode,
    DateOnly Date,
    decimal Days) : IDomainEvent;

/// <summary>An approved leave was cancelled: its days come back (E12).</summary>
public sealed record LeaveRequestCancelled(LeaveRequestId RequestId, EmploymentId EmploymentId, string LeaveTypeCode, DateOnly Date) : IDomainEvent;

/// <summary>A started leave ended early: <paramref name="Days"/> come back from <paramref name="Date"/>.</summary>
public sealed record LeaveRequestDaysReturned(LeaveRequestId RequestId, EmploymentId EmploymentId, string LeaveTypeCode, DateOnly Date, decimal Days) : IDomainEvent;
