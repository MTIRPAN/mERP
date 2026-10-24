using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.HR.Infrastructure.Leave;

namespace Merp.Modules.HR.UnitTests.Leave;

/// <summary>PLAN L3 acceptance: the balance scenarios of docs/domain/izin-kurallari.md §8.2.</summary>
public sealed class LeaveAccountTests
{
    private static readonly LedgerStamp Stamp = new(new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero), null);
    private static readonly AnnualLeaveRules Legal = LeaveSeeder.LegalRules();

    private static LeaveAccount Account() =>
        LeaveAccount.Open(EmployeeId.From(Guid.CreateVersion7()), EmploymentId.From(Guid.CreateVersion7()), Guid.CreateVersion7(), "YILLIK");

    private static IReadOnlyList<AccrualEvent> Due(DateOnly seniority, DateOnly after, DateOnly until, AnnualLeaveRules? rules = null, DateOnly? birth = null) =>
        LeaveAccruals.Due(_ => rules ?? Legal, seniority, birth, after, until, (_, _) => false);

    [Fact]
    public void E1_BeforeTheFirstAnniversary_NothingIsEarned_AndARequestDoesNotFit_ThenFourteenDaysAreEarned()
    {
        var account = Account();
        var seniority = new DateOnly(2025, 11, 10);

        Due(seniority, new DateOnly(2025, 11, 9), new DateOnly(2026, 10, 5)).ShouldBeEmpty();
        account.CanTake(days: 3, pending: 0, advanceLimit: 0).ShouldBeFalse();

        var due = Due(seniority, new DateOnly(2025, 11, 9), new DateOnly(2026, 11, 10)).ShouldHaveSingleItem();
        due.ShouldBe(new AccrualEvent(new DateOnly(2026, 11, 10), "2026-11-10", 14));
        account.Accrue(due.Date, due.PeriodKey, due.Days, Stamp).Value.ShouldBeTrue();
        account.Balance.ShouldBe(14);
    }

    [Fact]
    public void E2_TheCatchUp_WritesEachMissedAnniversaryWithItsOwnDays()
    {
        var due = Due(new DateOnly(2021, 3, 15), after: new DateOnly(2024, 12, 31), until: new DateOnly(2026, 3, 15));

        due.ShouldBe([new(new DateOnly(2025, 3, 15), "2025-03-15", 14), new(new DateOnly(2026, 3, 15), "2026-03-15", 20)]);
    }

    [Fact]
    public void E8_RunningTwiceOnTheSameDay_WritesOneMovement()
    {
        var account = Account();

        account.Accrue(new DateOnly(2026, 3, 15), "2026-03-15", 20, Stamp).Value.ShouldBeTrue();
        account.Accrue(new DateOnly(2026, 3, 15), "2026-03-15", 20, Stamp).Value.ShouldBeFalse();

        account.Entries.ShouldHaveSingleItem();
        account.Balance.ShouldBe(20);
    }

    [Fact]
    public void E9_AGroupTransfer_MovesTheBalance_AndClosesTheSource()
    {
        var source = Account();
        var target = Account();
        source.Accrue(new DateOnly(2026, 3, 15), "2026-03-15", 14, Stamp);
        source.RecordRequest(new DateOnly(2026, 5, 4), 6, Guid.CreateVersion7(), Stamp);

        var moved = source.TransferOut(new DateOnly(2026, 6, 30), target.EmploymentId.Value, Stamp).Value;
        target.TransferIn(new DateOnly(2026, 7, 1), moved, source.EmploymentId.Value, Stamp);

        moved.ShouldBe(8);
        source.Entries[^1].ShouldSatisfyAllConditions(e => e.Quantity.ShouldBe(-8), e => e.Source.ShouldBe(LeaveLedgerSource.Transfer));
        source.Balance.ShouldBe(0);
        source.ClosedOn.ShouldBe(new DateOnly(2026, 6, 30));
        target.Entries.ShouldHaveSingleItem().Quantity.ShouldBe(8);
        source.Accrue(new DateOnly(2027, 3, 15), "2027-03-15", 14, Stamp).Error.ShouldBe(LeaveAccountErrors.Closed);
    }

    [Theory]
    [InlineData(7, true)] // E10: advance limit 5, balance 3, 7 days → −4
    [InlineData(9, false)] // E10b: 9 days → −6, more than the advance
    public void E10_AnAdvance_LetsTheBalanceGoBelowZero_UpToItsLimit(int days, bool fits)
    {
        var account = Account();
        account.Adjust(new DateOnly(2026, 1, 1), 3, "Devir", Stamp);

        account.CanTake(days, pending: 0, advanceLimit: 5).ShouldBe(fits);
    }

    [Fact]
    public void PendingRequests_AreReservedFromTheBalance() =>
        Account().ShouldSatisfyAllConditions(account =>
        {
            account.Adjust(new DateOnly(2026, 1, 1), 10, "Devir", Stamp);
            account.CanTake(6, pending: 5, advanceLimit: 0).ShouldBeFalse();
            account.CanTake(5, pending: 5, advanceLimit: 0).ShouldBeTrue();
        });

    [Fact]
    public void E12_ACancelledRequest_GivesItsDaysBack()
    {
        var account = Account();
        var request = Guid.CreateVersion7();
        account.Accrue(new DateOnly(2026, 3, 15), "2026-03-15", 14, Stamp);

        account.RecordRequest(new DateOnly(2026, 7, 6), 5, request, Stamp).IsSuccess.ShouldBeTrue();
        account.CancelRequest(new DateOnly(2026, 7, 1), request, Stamp).IsSuccess.ShouldBeTrue();

        account.Entries.Select(e => (e.Source, e.Quantity)).ShouldBe(
            [(LeaveLedgerSource.Accrual, 14m), (LeaveLedgerSource.Request, -5m), (LeaveLedgerSource.RequestCancel, 5m)]);
        account.Balance.ShouldBe(14);
        account.Used.ShouldBe(0);
        account.CancelRequest(new DateOnly(2026, 7, 2), request, Stamp).Error.ShouldBe(LeaveAccountErrors.NothingToCancel);
    }

    [Fact]
    public void ACorrection_NeedsAReason_AndAnOpeningBalanceIsOnce()
    {
        var account = Account();

        account.Adjust(new DateOnly(2026, 1, 1), 2, " ", Stamp).Error.ShouldBe(LeaveAccountErrors.NoteRequired);
        account.OpeningBalance(new DateOnly(2025, 12, 31), 12.5m, null, Stamp).IsSuccess.ShouldBeTrue();
        account.OpeningBalance(new DateOnly(2025, 12, 31), 1, null, Stamp).Error.ShouldBe(LeaveAccountErrors.OpeningBalanceExists);
        account.Earned.ShouldBe(12.5m);
    }

    [Fact]
    public void Closing_TakesTheRestOut_AndAnnouncesIt()
    {
        var account = Account();
        account.Accrue(new DateOnly(2026, 3, 15), "2026-03-15", 14, Stamp);
        account.RecordRequest(new DateOnly(2026, 5, 4), 4, Guid.CreateVersion7(), Stamp);

        account.Close(new DateOnly(2026, 9, 30), Stamp).Value.ShouldBe(10);

        account.Balance.ShouldBe(0);
        account.Entries[^1].Source.ShouldBe(LeaveLedgerSource.Termination);
        account.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<LeaveAccountClosed>().Remaining.ShouldBe(10);
    }

    [Fact]
    public void MonthlyAccrual_EarnsATwelfthOfTheServiceYearInProgress_EachMonth()
    {
        var monthly = Legal with { AccrualMethod = AccrualMethod.MonthlyAccrual };

        var due = Due(new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 15), new DateOnly(2026, 4, 15), monthly);

        due.Select(d => (d.PeriodKey, d.Days)).ShouldBe([("2026-02", 1.17m), ("2026-03", 1.17m), ("2026-04", 1.17m)]);
    }

    [Fact]
    public void CalendarYearAccrual_EarnsOnTheFirstOfJanuary_ForTheYearsCompleted()
    {
        var yearly = Legal with { AccrualMethod = AccrualMethod.CalendarYearLumpSum };

        var due = Due(new DateOnly(2020, 6, 1), new DateOnly(2024, 12, 31), new DateOnly(2026, 1, 1), yearly);

        due.Select(d => (d.PeriodKey, d.Days)).ShouldBe([("2025", 14m), ("2026", 20m)]);
    }

    [Fact]
    public void TheAgeOnTheAnniversary_AppliesTheAgeMinimum()
    {
        var due = Due(new DateOnly(2023, 6, 1), new DateOnly(2026, 5, 31), new DateOnly(2026, 6, 1), birth: new DateOnly(1976, 2, 10));

        due.ShouldHaveSingleItem().Days.ShouldBe(20);
    }
}
