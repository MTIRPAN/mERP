using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Settings;
using Merp.Modules.HR.Application.Employees;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Contracts.Employees;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Leave;

/// <summary>
/// Writes the entitlements due to an employment up to <paramref name="Today"/> (default: today in the company's time
/// zone), from the accrual start setting on (D43), and closes the balance of an employment that has ended. Idempotent:
/// periods already accrued are skipped (E8). Sent by the nightly accrual job for each employment.
/// </summary>
public sealed record AccrueEmploymentLeaveCommand(Guid EmploymentId, DateOnly? Today = null) : ICommand<int>;

/// <summary>The employments the accrual job looks at: open ones, and ended ones whose balance is not closed yet.</summary>
public sealed record GetLeaveAccrualCandidatesQuery : IQuery<IReadOnlyList<Guid>>;

/// <summary>An employment's leave balances with their movements (PLAN L3, D43).</summary>
public sealed record GetLeaveBalancesQuery(Guid EmployeeId, Guid EmploymentId) : IQuery<IReadOnlyList<LeaveBalanceItem>>, IRequirePermission
{
    public string Permission => HrPermissions.Leave.Balances.Read;
}

/// <param name="Date">The day it counts from.</param>
/// <param name="Quantity">Days in (+) or out (−).</param>
/// <param name="Source">Why.</param>
/// <param name="PeriodKey">The accrual period.</param>
/// <param name="Note">The reason of a correction.</param>
/// <param name="RecordedAt">When it was written.</param>
public sealed record LeaveLedgerItem(DateOnly Date, decimal Quantity, LeaveLedgerSource Source, string? PeriodKey, string? Note, DateTimeOffset RecordedAt);

/// <param name="Days">The next entitlement's days.</param>
/// <param name="Date">Its day.</param>
public sealed record NextAccrual(DateOnly Date, decimal Days);

/// <remarks>
/// Balance is the sum of the movements; Earned the days in, Used the approved requests less the cancelled ones,
/// Pending the requests waiting for approval or correction; Available = Balance − Pending. NextAccrual is null when no policy gives
/// one within a year or the balance is closed.
/// </remarks>
public sealed record LeaveBalanceItem(
    string LeaveTypeCode,
    string LeaveTypeName,
    decimal Balance,
    decimal Earned,
    decimal Used,
    decimal Pending,
    decimal Available,
    DateOnly? ClosedOn,
    NextAccrual? NextAccrual,
    IReadOnlyList<LeaveLedgerItem> Entries);

/// <summary>HR corrects a balance, in or out; the reason is required (D43).</summary>
public sealed record AdjustLeaveBalanceCommand(string? LeaveTypeCode, DateOnly Date, decimal Quantity, string? Note) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid EmployeeId { get; init; }

    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid EmploymentId { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Leave.Balances.Manage;
}

public static class LeaveBalanceErrors
{
    public const string NoBalanceTypeCode = "hr.leave.errors.no_balance_type";
}

/// <summary>The leave types with a balance and the accounts that hold them (D43).</summary>
internal static class LeaveBalanceTypes
{
    /// <summary>Active types that use their own balance (YILLIK), of every company or of <paramref name="companyId"/>.</summary>
    public static Task<List<LeaveType>> ForCompanyAsync(IHrDbContext db, Guid companyId, CancellationToken cancellationToken) =>
        db.LeaveTypes.AsNoTracking()
            .Where(t => t.IsActive && t.DeductsFrom == t.Code && (t.CompanyId == null || t.CompanyId == companyId))
            .OrderBy(t => t.Code)
            .ToListAsync(cancellationToken);

    /// <summary>The balance the annual leave policy accrues into: the company's annual type, else every company's.</summary>
    public static async Task<LeaveType?> AnnualAsync(IHrDbContext db, Guid companyId, CancellationToken cancellationToken) =>
        (await ForCompanyAsync(db, companyId, cancellationToken))
            .Where(t => t.Category == LeaveCategory.Annual)
            .OrderBy(t => t.CompanyId is null)
            .FirstOrDefault();

    /// <summary>The account of an employment and a type, opened when missing.</summary>
    public static async Task<LeaveAccount> AccountAsync(IHrDbContext db, Employee employee, Employment employment, string code, CancellationToken cancellationToken)
    {
        var account = await db.LeaveAccounts.Include(a => a.Entries)
            .SingleOrDefaultAsync(a => a.EmploymentId == employment.Id && a.LeaveTypeCode == code, cancellationToken);
        if (account is null)
        {
            account = LeaveAccount.Open(employee.Id, employment.Id, employment.CompanyId, code);
            db.LeaveAccounts.Add(account);
        }

        return account;
    }

    public static LedgerStamp Stamp(TimeProvider timeProvider, ICurrentUser currentUser) =>
        new(timeProvider.GetUtcNow(), currentUser.IsSystem ? null : currentUser.UserId);

    /// <summary>The rules of a policy on a day, and whether a criterion matches the employee (as they are today).</summary>
    public static (Func<DateOnly, AnnualLeaveRules?> RulesOn, Func<DateOnly, PolicyCriterion, bool> Matches) Policy(ChosenLeavePolicy chosen) =>
        (day => chosen.Policy?.VersionOn(day)?.Rules, (_, criterion) => criterion.Matches(chosen.Subject));
}

internal sealed class AdjustLeaveBalanceCommandValidator : AbstractValidator<AdjustLeaveBalanceCommand>
{
    public AdjustLeaveBalanceCommandValidator()
    {
        RuleFor(c => c.LeaveTypeCode).NotEmpty();
        RuleFor(c => c.Quantity).NotEqual(0).InclusiveBetween(-999, 999);
        RuleFor(c => c.Note).NotEmpty().WithErrorCode("hr.leave.errors.note_required").MaximumLength(LeaveLedgerEntry.NoteMaxLength);
    }
}

internal sealed class GetLeaveAccrualCandidatesQueryHandler(IHrDbContext db) : IQueryHandler<GetLeaveAccrualCandidatesQuery, IReadOnlyList<Guid>>
{
    public async Task<Result<IReadOnlyList<Guid>>> Handle(GetLeaveAccrualCandidatesQuery request, CancellationToken cancellationToken)
    {
        var open = await db.Employees.AsNoTracking()
            .SelectMany(e => e.Employments)
            .Where(m => m.TerminationDate == null)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);
        var unclosed = await db.LeaveAccounts.AsNoTracking()
            .Where(a => a.ClosedOn == null)
            .Select(a => a.EmploymentId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return open.Union(unclosed).Select(id => id.Value).ToList();
    }
}

internal sealed class AccrueEmploymentLeaveCommandHandler(
    IHrDbContext db,
    LeavePolicyResolver resolver,
    ISettings settings,
    IOrganizationLookup organization,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<AccrueEmploymentLeaveCommand, int>
{
    public async Task<Result<int>> Handle(AccrueEmploymentLeaveCommand request, CancellationToken cancellationToken)
    {
        var employee = await EmployeeRules.LoadAsync(db, await EmployeeIdAsync(request.EmploymentId, cancellationToken), cancellationToken);
        var employment = employee?.Employments.SingleOrDefault(m => m.Id == EmploymentId.From(request.EmploymentId));
        if (employee is null || employment is null)
        {
            return CommonErrors.NotFound;
        }

        var today = request.Today ?? await LeaveTypeRules.TodayAsync(organization, timeProvider, employment.CompanyId, cancellationToken);
        var type = await LeaveBalanceTypes.AnnualAsync(db, employment.CompanyId, cancellationToken);
        var start = await settings.GetAsync<DateOnly?>(HrSettingDefinitions.LeaveAccrualStart, employment.CompanyId, cancellationToken);
        // Without the accrual start nothing is accrued yet; an open employment then needs no account
        if (type is null || (start is null && employment.TerminationDate is null))
        {
            return 0;
        }

        var account = await LeaveBalanceTypes.AccountAsync(db, employee, employment, type.Code, cancellationToken);
        if (account.ClosedOn is not null)
        {
            return 0;
        }

        var stamp = LeaveBalanceTypes.Stamp(timeProvider, currentUser);
        var accrued = 0;
        var last = employment.TerminationDate is { } end && end < today ? end : today;
        if (start is { } from)
        {
            var chosen = await resolver.ChooseAsync(employee, employment, last, cancellationToken);
            var (rulesOn, matches) = LeaveBalanceTypes.Policy(chosen);
            var after = new[] { from.AddDays(-1), employment.HireDate.AddDays(-1) }.Max();
            foreach (var due in LeaveAccruals.Due(rulesOn, employment.SeniorityDate, employee.BirthDate, after, last, matches))
            {
                if (account.Accrue(due.Date, due.PeriodKey, due.Days, stamp) is { IsSuccess: true, Value: true })
                {
                    accrued++;
                }
            }
        }

        // An employment that has ended closes its balance after its last day's entitlements
        if (employment.TerminationDate is { } terminated && terminated < today)
        {
            account.Close(terminated, stamp);
        }

        return accrued;
    }

    private async Task<Guid> EmployeeIdAsync(Guid employmentId, CancellationToken cancellationToken) =>
        await db.Employees.AsNoTracking()
            .Where(e => e.Employments.Any(m => m.Id == EmploymentId.From(employmentId)))
            .Select(e => e.Id.Value)
            .SingleOrDefaultAsync(cancellationToken);
}

internal sealed class GetLeaveBalancesQueryHandler(IHrDbContext db, IAccessScopeProvider scopes, LeaveBalanceReader reader)
    : IQueryHandler<GetLeaveBalancesQuery, IReadOnlyList<LeaveBalanceItem>>
{
    public async Task<Result<IReadOnlyList<LeaveBalanceItem>>> Handle(GetLeaveBalancesQuery request, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ForCompaniesAsync(scopes, request.Permission, cancellationToken);
        var employee = await db.Employees.AsNoTracking()
            .Include(e => e.Employments)
            .ThenInclude(m => m.Assignments)
            .SingleOrDefaultAsync(e => e.Id == EmployeeId.From(request.EmployeeId), cancellationToken);
        var employment = employee?.Employments.SingleOrDefault(m => m.Id == EmploymentId.From(request.EmploymentId));
        if (employee is null || employment is null || !scope.CoversCompany(employment.CompanyId))
        {
            return CommonErrors.NotFound;
        }

        return Result.Success(await reader.ReadAsync(employee, employment, cancellationToken));
    }
}

/// <summary>
/// The balances of an employment (D43): every type with a balance, its movements, the requests waiting for approval
/// on it (D44) and the next entitlement of the annual leave.
/// </summary>
public sealed class LeaveBalanceReader(
    IHrDbContext db,
    LeavePolicyResolver resolver,
    IOrganizationLookup organization,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<LeaveBalanceItem>> ReadAsync(Employee employee, Employment employment, CancellationToken cancellationToken)
    {
        var types = await LeaveBalanceTypes.ForCompanyAsync(db, employment.CompanyId, cancellationToken);
        var accounts = await db.LeaveAccounts.AsNoTracking().Include(a => a.Entries)
            .Where(a => a.EmploymentId == employment.Id)
            .ToListAsync(cancellationToken);
        var pending = (await db.LeaveRequests.AsNoTracking()
                .Where(r => r.EmploymentId == employment.Id && r.DeductsFrom != null && LeaveRequestStatuses.Reserving.Contains(r.Status))
                .Select(r => new { r.DeductsFrom, r.Days })
                .ToListAsync(cancellationToken))
            .GroupBy(r => r.DeductsFrom!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Days), StringComparer.Ordinal);
        var today = await LeaveTypeRules.TodayAsync(organization, timeProvider, employment.CompanyId, cancellationToken);
        var annual = types.Where(t => t.Category == LeaveCategory.Annual).OrderBy(t => t.CompanyId is null).FirstOrDefault();
        NextAccrual? next = null;
        if (annual is not null && employment.IsOpen)
        {
            var chosen = await resolver.ChooseAsync(employee, employment, today, cancellationToken);
            var (rulesOn, matches) = LeaveBalanceTypes.Policy(chosen);
            next = LeaveAccruals.Next(rulesOn, employment.SeniorityDate, employee.BirthDate, today, matches) is { } due ? new NextAccrual(due.Date, due.Days) : null;
        }

        var names = types.ToDictionary(t => t.Code, t => t.Name.Get(currentUser.Culture, "tr-TR"), StringComparer.Ordinal);
        var codes = types.Select(t => t.Code).Union(accounts.Select(a => a.LeaveTypeCode)).Distinct(StringComparer.Ordinal);
        return codes
            .Select(code =>
            {
                var account = accounts.SingleOrDefault(a => a.LeaveTypeCode == code);
                var waiting = pending.GetValueOrDefault(code);
                var balance = account?.Balance ?? 0;
                return new LeaveBalanceItem(
                    code,
                    names.GetValueOrDefault(code, code),
                    balance,
                    account?.Earned ?? 0,
                    account?.Used ?? 0,
                    waiting,
                    balance - waiting,
                    account?.ClosedOn,
                    code == annual?.Code && account?.ClosedOn is null ? next : null,
                    [.. (account?.Entries ?? []).OrderByDescending(e => e.Date).ThenByDescending(e => e.RecordedAt)
                        .Select(e => new LeaveLedgerItem(e.Date, e.Quantity, e.Source, e.PeriodKey, e.Note, e.RecordedAt))]);
            })
            .ToList();
    }
}

internal sealed class AdjustLeaveBalanceCommandHandler(IHrDbContext db, IAccessScopeProvider scopes, ICurrentUser currentUser, TimeProvider timeProvider)
    : ICommandHandler<AdjustLeaveBalanceCommand>
{
    public async Task<Result> Handle(AdjustLeaveBalanceCommand request, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ForCompaniesAsync(scopes, request.Permission, cancellationToken);
        var employee = await EmployeeRules.LoadAsync(db, request.EmployeeId, cancellationToken);
        var employment = employee?.Employments.SingleOrDefault(m => m.Id == EmploymentId.From(request.EmploymentId));
        if (employee is null || employment is null || !scope.CoversCompany(employment.CompanyId))
        {
            return CommonErrors.NotFound;
        }

        var code = request.LeaveTypeCode!.Trim().ToUpperInvariant();
        if (!(await LeaveBalanceTypes.ForCompanyAsync(db, employment.CompanyId, cancellationToken)).Any(t => t.Code == code))
        {
            return Error.Validation([new FieldError("leaveTypeCode", LeaveBalanceErrors.NoBalanceTypeCode)]);
        }

        var account = await LeaveBalanceTypes.AccountAsync(db, employee, employment, code, cancellationToken);
        return account.Adjust(request.Date, request.Quantity, request.Note, LeaveBalanceTypes.Stamp(timeProvider, currentUser));
    }
}

/// <summary>
/// A transfer within the group moves the balance of the source employment to the new one when asked; otherwise the
/// source is closed (paid) by the accrual job after its last day (E9, D43).
/// </summary>
internal sealed class MoveLeaveBalanceOnGroupTransfer(IHrDbContext db, ICurrentUser currentUser, TimeProvider timeProvider)
    : IDomainEventHandler<EmploymentTransferredInGroup>
{
    public async Task Handle(EmploymentTransferredInGroup domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        if (!domainEvent.TransferLeaveBalance)
        {
            return;
        }

        var stamp = LeaveBalanceTypes.Stamp(timeProvider, currentUser);
        var sources = await db.LeaveAccounts.Include(a => a.Entries)
            .Where(a => a.EmploymentId == domainEvent.SourceEmploymentId && a.ClosedOn == null)
            .ToListAsync(cancellationToken);
        foreach (var source in sources)
        {
            var moved = source.TransferOut(domainEvent.TransferDate.AddDays(-1), domainEvent.TargetEmploymentId.Value, stamp);
            if (moved.IsFailure)
            {
                continue;
            }

            var target = LeaveAccount.Open(domainEvent.EmployeeId, domainEvent.TargetEmploymentId, domainEvent.TargetCompanyId, source.LeaveTypeCode);
            target.TransferIn(domainEvent.TransferDate, moved.Value, domainEvent.SourceEmploymentId.Value, stamp);
            db.LeaveAccounts.Add(target);
        }
    }
}

/// <summary>Tells payroll what was left of a closed balance (D43).</summary>
internal sealed class PublishLeaveBalanceClosed(IIntegrationEventPublisher publisher) : IDomainEventHandler<LeaveAccountClosed>
{
    public Task Handle(LeaveAccountClosed domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return publisher.PublishAsync(
            new EmployeeLeaveBalanceClosed(domainEvent.EmployeeId.Value, domainEvent.EmploymentId.Value, domainEvent.LeaveTypeCode, domainEvent.Date, domainEvent.Remaining)
            {
                CompanyId = domainEvent.CompanyId,
            },
            cancellationToken);
    }
}

/// <summary>An approved leave takes its days from the balance, in the same transaction (D44, rule 7).</summary>
internal sealed class RecordApprovedLeave(IHrDbContext db, ICurrentUser currentUser, TimeProvider timeProvider) : IDomainEventHandler<LeaveRequestApproved>
{
    public async Task Handle(LeaveRequestApproved domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        if (domainEvent.Days <= 0)
        {
            return;
        }

        var account = await db.LeaveAccounts.Include(a => a.Entries)
            .SingleOrDefaultAsync(a => a.EmploymentId == domainEvent.EmploymentId && a.LeaveTypeCode == domainEvent.LeaveTypeCode, cancellationToken);
        if (account is null)
        {
            account = LeaveAccount.Open(domainEvent.EmployeeId, domainEvent.EmploymentId, domainEvent.CompanyId, domainEvent.LeaveTypeCode);
            db.LeaveAccounts.Add(account);
        }

        // A closed balance (the employment ended meanwhile) takes nothing more
        account.RecordRequest(domainEvent.Date, domainEvent.Days, domainEvent.RequestId.Value, LeaveBalanceTypes.Stamp(timeProvider, currentUser));
    }
}

/// <summary>A cancelled leave gives its days back (E12).</summary>
internal sealed class GiveBackCancelledLeave(IHrDbContext db, ICurrentUser currentUser, TimeProvider timeProvider) : IDomainEventHandler<LeaveRequestCancelled>
{
    public async Task Handle(LeaveRequestCancelled domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var account = await db.LeaveAccounts.Include(a => a.Entries)
            .SingleOrDefaultAsync(a => a.EmploymentId == domainEvent.EmploymentId && a.LeaveTypeCode == domainEvent.LeaveTypeCode, cancellationToken);
        account?.CancelRequest(domainEvent.Date, domainEvent.RequestId.Value, LeaveBalanceTypes.Stamp(timeProvider, currentUser));
    }
}

/// <summary>A leave ended early gives back the days no longer taken (D44).</summary>
internal sealed class GiveBackLeaveDays(IHrDbContext db, ICurrentUser currentUser, TimeProvider timeProvider) : IDomainEventHandler<LeaveRequestDaysReturned>
{
    public async Task Handle(LeaveRequestDaysReturned domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var account = await db.LeaveAccounts.Include(a => a.Entries)
            .SingleOrDefaultAsync(a => a.EmploymentId == domainEvent.EmploymentId && a.LeaveTypeCode == domainEvent.LeaveTypeCode, cancellationToken);
        account?.GiveBack(domainEvent.Date, domainEvent.RequestId.Value, domainEvent.Days, LeaveBalanceTypes.Stamp(timeProvider, currentUser));
    }
}
