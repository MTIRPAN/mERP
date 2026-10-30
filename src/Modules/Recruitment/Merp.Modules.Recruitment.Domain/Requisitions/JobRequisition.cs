using System.Text.Json.Serialization;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Recruitment.Domain.Requisitions;

public readonly record struct JobRequisitionId(Guid Value) : IEntityId<JobRequisitionId>
{
    public static JobRequisitionId From(Guid value) => new(value);
}

/// <summary>Why the people are needed (PLAN R2).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RequisitionReason>))]
public enum RequisitionReason
{
    /// <summary>A new seat.</summary>
    NewHeadcount,

    /// <summary>In place of someone who left or is leaving.</summary>
    Replacement,
}

[JsonConverter(typeof(JsonStringEnumConverter<RequisitionEmploymentType>))]
public enum RequisitionEmploymentType
{
    Indefinite,
    FixedTerm,
    PartTime,
    Seasonal,
    Intern,
}

/// <summary>Where a requisition stands (D48).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RequisitionStatus>))]
public enum RequisitionStatus
{
    Draft,

    /// <summary>Waits for approval.</summary>
    Pending,

    /// <summary>An approver sent it back for correction.</summary>
    Returned,

    /// <summary>Approved: an opening may be published for it (R3).</summary>
    Approved,

    Rejected,

    Cancelled,

    /// <summary>Every seat it asked for was filled (R7).</summary>
    Filled,
}

/// <summary>What a requisition asks for, as the application checked it.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="OrgUnitId">The unit: the position's for an existing position.</param>
/// <param name="PositionId">An existing position with vacant seats; null for a new position.</param>
/// <param name="NewPositionTitle">The title of a new position proposed.</param>
/// <param name="NewPositionLevel">Its level, e.g. M2.</param>
/// <param name="Headcount">People needed.</param>
/// <param name="Reason">New seat or replacement.</param>
/// <param name="ReplacedEmployeeId">The person replaced (HR's employee).</param>
/// <param name="EmploymentType">The kind of employment.</param>
/// <param name="DesiredStartDate">When the people should start.</param>
/// <param name="SalaryMin">Lowest monthly gross salary planned (sensitive).</param>
/// <param name="SalaryMax">Highest monthly gross salary planned (sensitive).</param>
/// <param name="Currency">ISO currency of the salary range.</param>
/// <param name="Justification">Why the people are needed.</param>
/// <param name="HiringManagerUserId">The manager the people will work for.</param>
public sealed record RequisitionTerms(
    Guid CompanyId,
    Guid OrgUnitId,
    Guid? PositionId,
    string? NewPositionTitle,
    string? NewPositionLevel,
    int Headcount,
    RequisitionReason Reason,
    Guid? ReplacedEmployeeId,
    RequisitionEmploymentType EmploymentType,
    DateOnly? DesiredStartDate,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? Currency,
    string Justification,
    Guid HiringManagerUserId);

/// <summary>
/// A request to hire people (personel talebi, PLAN R2, D48): for vacant seats of an existing position or for a new
/// position proposed, approved through the approval engine (<c>recruitment.requisition</c>). Once approved an opening
/// may be published for it (R3); hires fill its seats (R7).
/// </summary>
[Audited]
public sealed class JobRequisition : AuditableAggregateRoot<JobRequisitionId>, ICompanyScoped
{
    public const int NumberMaxLength = 30;
    public const int TitleMaxLength = 150;
    public const int LevelMaxLength = 20;
    public const int JustificationMaxLength = 2000;
    public const int CommentMaxLength = 500;
    public const int CurrencyLength = 3;
    public const int MaxHeadcount = 99;

    private JobRequisition()
    {
    }

    /// <summary>P5 <c>recruitment.requisition</c>, e.g. PT-2026-00012.</summary>
    public string Number { get; private set; } = null!;

    public Guid CompanyId { get; private set; }

    public Guid OrgUnitId { get; private set; }

    public Guid? PositionId { get; private set; }

    public string? NewPositionTitle { get; private set; }

    public string? NewPositionLevel { get; private set; }

    public int Headcount { get; private set; }

    public RequisitionReason Reason { get; private set; }

    public Guid? ReplacedEmployeeId { get; private set; }

    public RequisitionEmploymentType EmploymentType { get; private set; }

    public DateOnly? DesiredStartDate { get; private set; }

    [Sensitive]
    public decimal? SalaryMin { get; private set; }

    [Sensitive]
    public decimal? SalaryMax { get; private set; }

    public string? Currency { get; private set; }

    public string Justification { get; private set; } = null!;

    public Guid HiringManagerUserId { get; private set; }

    public Guid RequesterUserId { get; private set; }

    public RequisitionStatus Status { get; private set; }

    /// <summary>Seats filled by hires (R7).</summary>
    public int FilledCount { get; private set; }

    public string? ReturnComment { get; private set; }

    public string? CancellationReason { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public bool IsNewPosition => PositionId is null;

    /// <summary>Holds seats of its position: asked for and not refused, or approved and not filled.</summary>
    public bool ReservesSeats => Status is RequisitionStatus.Pending or RequisitionStatus.Returned or RequisitionStatus.Approved;

    public static JobRequisition Draft(string number, Guid requesterUserId, RequisitionTerms details)
    {
        var requisition = new JobRequisition
        {
            Id = EntityId.New<JobRequisitionId>(),
            Number = Text.Required(number, NumberMaxLength, nameof(number)),
            RequesterUserId = requesterUserId,
            Status = RequisitionStatus.Draft,
        };
        requisition.Set(details);
        return requisition;
    }

    /// <summary>Corrects a draft or a returned requisition.</summary>
    public Result Update(RequisitionTerms details)
    {
        if (Status is not (RequisitionStatus.Draft or RequisitionStatus.Returned))
        {
            return JobRequisitionErrors.NotEditable;
        }

        Set(details);
        return Result.Success();
    }

    /// <summary>Sends a draft or a returned requisition for approval.</summary>
    public Result Submit(DateTimeOffset at)
    {
        if (Status is not (RequisitionStatus.Draft or RequisitionStatus.Returned))
        {
            return JobRequisitionErrors.NotEditable;
        }

        Status = RequisitionStatus.Pending;
        ReturnComment = null;
        SubmittedAt = at;
        return Result.Success();
    }

    /// <summary>The flow ended; ignored unless it waits for approval.</summary>
    public void Complete(bool approved, DateTimeOffset at)
    {
        if (Status == RequisitionStatus.Pending)
        {
            Status = approved ? RequisitionStatus.Approved : RequisitionStatus.Rejected;
            DecidedAt = at;
        }
    }

    /// <summary>An approver returned it for correction; ignored unless it waits.</summary>
    public void Return(string comment)
    {
        if (Status == RequisitionStatus.Pending)
        {
            Status = RequisitionStatus.Returned;
            ReturnComment = Text.Optional(comment, CommentMaxLength, nameof(comment));
        }
    }

    /// <summary>
    /// Cancels a requisition that is not decided yet or approved and not filled. Returns whether an approval was open
    /// and has to be closed.
    /// </summary>
    public Result<bool> Cancel(string? reason, DateTimeOffset at)
    {
        if (Status is not (RequisitionStatus.Draft or RequisitionStatus.Pending or RequisitionStatus.Returned or RequisitionStatus.Approved))
        {
            return JobRequisitionErrors.NotCancellable;
        }

        var flowOpen = Status is RequisitionStatus.Pending or RequisitionStatus.Returned;
        Status = RequisitionStatus.Cancelled;
        CancellationReason = Text.Optional(reason, CommentMaxLength, nameof(reason));
        DecidedAt = at;
        return flowOpen;
    }

    /// <summary>A hire filled one of the seats of an approved requisition (D54); it is Filled with the last one.</summary>
    public void Fill()
    {
        if (Status != RequisitionStatus.Approved)
        {
            return;
        }

        FilledCount++;
        if (FilledCount >= Headcount)
        {
            Status = RequisitionStatus.Filled;
        }
    }

    private void Set(RequisitionTerms details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (details.Headcount is < 1 or > MaxHeadcount)
        {
            throw new ArgumentOutOfRangeException(nameof(details), details.Headcount, "A requisition asks for 1 to 99 people.");
        }

        if (details.PositionId is null && string.IsNullOrWhiteSpace(details.NewPositionTitle))
        {
            throw new ArgumentException("A new position needs a title.", nameof(details));
        }

        CompanyId = details.CompanyId;
        OrgUnitId = details.OrgUnitId;
        PositionId = details.PositionId;
        NewPositionTitle = details.PositionId is null ? Text.Required(details.NewPositionTitle, TitleMaxLength, nameof(details.NewPositionTitle)) : null;
        NewPositionLevel = details.PositionId is null ? Text.Optional(details.NewPositionLevel, LevelMaxLength, nameof(details.NewPositionLevel)) : null;
        Headcount = details.Headcount;
        Reason = details.Reason;
        ReplacedEmployeeId = details.Reason == RequisitionReason.Replacement ? details.ReplacedEmployeeId : null;
        EmploymentType = details.EmploymentType;
        DesiredStartDate = details.DesiredStartDate;
        SalaryMin = details.SalaryMin;
        SalaryMax = details.SalaryMax;
        Currency = details.SalaryMin is null && details.SalaryMax is null ? null : details.Currency?.Trim().ToUpperInvariant();
        Justification = Text.Required(details.Justification, JustificationMaxLength, nameof(details.Justification));
        HiringManagerUserId = details.HiringManagerUserId;
    }
}

public static class JobRequisitionErrors
{
    public static readonly Error NotEditable = Error.Conflict("recruitment.requisitions.errors.not_editable");
    public static readonly Error NotCancellable = Error.Conflict("recruitment.requisitions.errors.not_cancellable");
}
