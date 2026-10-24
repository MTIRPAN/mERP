using System.Text.Json.Serialization;
using Merp.Modules.HR.Domain.Employees;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;

namespace Merp.Modules.HR.Domain.Leave;

public readonly record struct LeaveAccountId(Guid Value) : IEntityId<LeaveAccountId>
{
    public static LeaveAccountId From(Guid value) => new(value);
}

public readonly record struct LeaveLedgerEntryId(Guid Value) : IEntityId<LeaveLedgerEntryId>
{
    public static LeaveLedgerEntryId From(Guid value) => new(value);
}

/// <summary>Why a balance moved (docs/domain/izin-kurallari.md §5).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LeaveLedgerSource>))]
public enum LeaveLedgerSource
{
    Accrual,

    /// <summary>An approved request (−).</summary>
    Request,

    /// <summary>An approved request cancelled (+).</summary>
    RequestCancel,

    /// <summary>HR's correction, with a reason.</summary>
    Adjustment,

    /// <summary>The balance brought from the previous system.</summary>
    OpeningBalance,

    /// <summary>Moved with a transfer within the group.</summary>
    Transfer,

    /// <summary>Closed when the employment ended; the rest is paid.</summary>
    Termination,
}

/// <summary>One movement of a balance: days in (+) or out (−), two decimals.</summary>
public sealed class LeaveLedgerEntry : Entity<LeaveLedgerEntryId>
{
    public const int NoteMaxLength = 500;
    public const int PeriodKeyMaxLength = 10;

    private LeaveLedgerEntry()
    {
    }

    public LeaveAccountId AccountId { get; private set; }

    public DateOnly Date { get; private set; }

    public decimal Quantity { get; private set; }

    public LeaveLedgerSource Source { get; private set; }

    /// <summary>The record behind it, e.g. the leave request.</summary>
    public Guid? SourceId { get; private set; }

    /// <summary>The accrual period, e.g. <c>2026-03-15</c> or <c>2026-07</c>; one accrual per period.</summary>
    public string? PeriodKey { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    /// <summary>Who recorded it; null for the system (the accrual job).</summary>
    public Guid? RecordedBy { get; private set; }

    internal static LeaveLedgerEntry Create(
        LeaveAccountId accountId,
        DateOnly date,
        decimal quantity,
        LeaveLedgerSource source,
        Guid? sourceId,
        string? periodKey,
        string? note,
        LedgerStamp stamp) => new()
        {
            Id = EntityId.New<LeaveLedgerEntryId>(),
            AccountId = accountId,
            Date = date,
            Quantity = Math.Round(quantity, 2, MidpointRounding.AwayFromZero),
            Source = source,
            SourceId = sourceId,
            PeriodKey = periodKey,
            Note = Text.Optional(note, NoteMaxLength, nameof(note)),
            RecordedAt = stamp.At,
            RecordedBy = stamp.By,
        };
}

/// <summary>When and by whom a movement is recorded.</summary>
public sealed record LedgerStamp(DateTimeOffset At, Guid? By);

/// <summary>
/// The balance of one leave type (YILLIK) of an employment (docs/domain/izin-kurallari.md §5, D43). The balance is
/// never stored: it is the sum of the movements. One account holds them all, so a catch-up of several accruals or a
/// closing is one change.
/// </summary>
[Audited]
public sealed class LeaveAccount : AuditableAggregateRoot<LeaveAccountId>, ICompanyScoped
{
    private readonly List<LeaveLedgerEntry> _entries = [];

    private LeaveAccount()
    {
    }

    public EmploymentId EmploymentId { get; private set; }

    public EmployeeId EmployeeId { get; private set; }

    public Guid CompanyId { get; private set; }

    public string LeaveTypeCode { get; private set; } = null!;

    /// <summary>The day the employment's balance was closed or moved; nothing is added afterwards.</summary>
    public DateOnly? ClosedOn { get; private set; }

    public IReadOnlyList<LeaveLedgerEntry> Entries => _entries.AsReadOnly();

    public decimal Balance => _entries.Sum(e => e.Quantity);

    /// <summary>Days in: accruals, the opening balance, transfers in and positive corrections.</summary>
    public decimal Earned => _entries.Where(e => e.Source is not (LeaveLedgerSource.Request or LeaveLedgerSource.RequestCancel or LeaveLedgerSource.Termination) && e.Quantity > 0).Sum(e => e.Quantity);

    /// <summary>Days taken: approved requests less those cancelled.</summary>
    public decimal Used => -_entries.Where(e => e.Source is LeaveLedgerSource.Request or LeaveLedgerSource.RequestCancel).Sum(e => e.Quantity);

    public static LeaveAccount Open(EmployeeId employeeId, EmploymentId employmentId, Guid companyId, string leaveTypeCode) => new()
    {
        Id = EntityId.New<LeaveAccountId>(),
        EmployeeId = employeeId,
        EmploymentId = employmentId,
        CompanyId = companyId,
        LeaveTypeCode = leaveTypeCode.Trim().ToUpperInvariant(),
    };

    public bool HasAccrual(string periodKey) => _entries.Any(e => e.Source == LeaveLedgerSource.Accrual && e.PeriodKey == periodKey);

    /// <summary>The entitlement of a period; a period already accrued is skipped, so the job may run again (E8).</summary>
    public Result<bool> Accrue(DateOnly date, string periodKey, decimal days, LedgerStamp stamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(periodKey);
        if (ClosedOn is not null)
        {
            return LeaveAccountErrors.Closed;
        }

        if (days <= 0 || HasAccrual(periodKey))
        {
            return false;
        }

        Add(date, days, LeaveLedgerSource.Accrual, null, periodKey, null, stamp);
        return true;
    }

    /// <summary>HR's correction, in or out; the reason is required.</summary>
    public Result Adjust(DateOnly date, decimal days, string? note, LedgerStamp stamp)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return LeaveAccountErrors.NoteRequired;
        }

        return Move(date, days, LeaveLedgerSource.Adjustment, null, note, stamp);
    }

    /// <summary>The balance brought from the previous system, once per account.</summary>
    public Result OpeningBalance(DateOnly date, decimal days, string? note, LedgerStamp stamp) =>
        _entries.Any(e => e.Source == LeaveLedgerSource.OpeningBalance)
            ? LeaveAccountErrors.OpeningBalanceExists
            : Move(date, days, LeaveLedgerSource.OpeningBalance, null, note, stamp);

    /// <summary>An approved request takes its days (−); <paramref name="requestId"/> is the request.</summary>
    public Result RecordRequest(DateOnly date, decimal days, Guid requestId, LedgerStamp stamp) =>
        days <= 0 ? LeaveAccountErrors.InvalidQuantity : Move(date, -days, LeaveLedgerSource.Request, requestId, null, stamp);

    /// <summary>A cancelled request gives back what it took (+, E12).</summary>
    public Result CancelRequest(DateOnly date, Guid requestId, LedgerStamp stamp)
    {
        var taken = -_entries.Where(e => e.SourceId == requestId && e.Source is LeaveLedgerSource.Request or LeaveLedgerSource.RequestCancel).Sum(e => e.Quantity);
        return taken <= 0 ? LeaveAccountErrors.NothingToCancel : Move(date, taken, LeaveLedgerSource.RequestCancel, requestId, null, stamp);
    }

    /// <summary>A leave ended early gives back part of what it took (+), at most what it still holds.</summary>
    public Result GiveBack(DateOnly date, Guid requestId, decimal days, LedgerStamp stamp)
    {
        var taken = -_entries.Where(e => e.SourceId == requestId && e.Source is LeaveLedgerSource.Request or LeaveLedgerSource.RequestCancel).Sum(e => e.Quantity);
        return days <= 0 || taken <= 0
            ? LeaveAccountErrors.NothingToCancel
            : Move(date, Math.Min(days, taken), LeaveLedgerSource.RequestCancel, requestId, null, stamp);
    }

    /// <summary>
    /// Whether <paramref name="days"/> fit the balance less <paramref name="pending"/> requests, going at most
    /// <paramref name="advanceLimit"/> days below zero (§6 rule 7, E1, E10).
    /// </summary>
    public bool CanTake(decimal days, decimal pending, decimal advanceLimit) => Balance - pending - days >= -advanceLimit;

    /// <summary>Moves the whole balance out to another employment of the group and closes the account (E9).</summary>
    public Result<decimal> TransferOut(DateOnly date, Guid targetEmploymentId, LedgerStamp stamp)
    {
        var balance = Balance;
        if (ClosedOn is not null)
        {
            return LeaveAccountErrors.Closed;
        }

        if (balance != 0)
        {
            Add(date, -balance, LeaveLedgerSource.Transfer, targetEmploymentId, null, null, stamp);
        }

        ClosedOn = date;
        return balance;
    }

    /// <summary>The balance moved in from the source employment of a transfer.</summary>
    public Result TransferIn(DateOnly date, decimal days, Guid sourceEmploymentId, LedgerStamp stamp) =>
        days == 0 ? Result.Success() : Move(date, days, LeaveLedgerSource.Transfer, sourceEmploymentId, null, stamp);

    /// <summary>Closes the account of an ended employment: the rest (or the advance taken) leaves it; returns what was left.</summary>
    public Result<decimal> Close(DateOnly date, LedgerStamp stamp)
    {
        if (ClosedOn is not null)
        {
            return LeaveAccountErrors.Closed;
        }

        var balance = Balance;
        if (balance != 0)
        {
            Add(date, -balance, LeaveLedgerSource.Termination, null, null, null, stamp);
        }

        ClosedOn = date;
        Raise(new LeaveAccountClosed(Id, EmployeeId, EmploymentId, CompanyId, LeaveTypeCode, date, balance));
        return balance;
    }

    private Result Move(DateOnly date, decimal days, LeaveLedgerSource source, Guid? sourceId, string? note, LedgerStamp stamp)
    {
        if (ClosedOn is not null)
        {
            return LeaveAccountErrors.Closed;
        }

        if (days == 0 || Math.Abs(days) > MaxQuantity)
        {
            return LeaveAccountErrors.InvalidQuantity;
        }

        Add(date, days, source, sourceId, null, note, stamp);
        return Result.Success();
    }

    private const decimal MaxQuantity = 999;

    private void Add(DateOnly date, decimal days, LeaveLedgerSource source, Guid? sourceId, string? periodKey, string? note, LedgerStamp stamp) =>
        _entries.Add(LeaveLedgerEntry.Create(Id, date, days, source, sourceId, periodKey, note, stamp));
}

public static class LeaveAccountErrors
{
    public static readonly Error Closed = Error.Validation([new FieldError("employmentId", "hr.leave.errors.account_closed")]);
    public static readonly Error NoteRequired = Error.Validation([new FieldError("note", "hr.leave.errors.note_required")]);
    public static readonly Error InvalidQuantity = Error.Validation([new FieldError("quantity", "hr.leave.errors.invalid_quantity")]);
    public static readonly Error OpeningBalanceExists = Error.Validation([new FieldError("quantity", "hr.leave.errors.opening_balance_exists")]);
    public static readonly Error NothingToCancel = Error.Validation([new FieldError("requestId", "hr.leave.errors.nothing_to_cancel")]);

    public static readonly Error InsufficientBalance = Error.Validation([new FieldError("days", "hr.leave.errors.insufficient_balance")]);
}

/// <summary>An employment's balance was closed; <paramref name="Remaining"/> left it (paid, or an advance owed when negative).</summary>
public sealed record LeaveAccountClosed(
    LeaveAccountId AccountId,
    EmployeeId EmployeeId,
    EmploymentId EmploymentId,
    Guid CompanyId,
    string LeaveTypeCode,
    DateOnly Date,
    decimal Remaining) : IDomainEvent;
