using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Leave;

namespace Merp.Modules.HR.UnitTests.Leave;

/// <summary>The life of a leave request (PLAN L4, D44): sending, approval, return, cancellation and early return.</summary>
public sealed class LeaveRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 1);
    private static readonly LeaveRequestContact NoContact = new(null, null, null, null);

    private static LeaveRequestPeriod Period(
        DateOnly start,
        DateOnly end,
        decimal days,
        string? deductsFrom = "YILLIK",
        LeaveDayPart startPart = LeaveDayPart.Full,
        LeaveDayPart endPart = LeaveDayPart.Full,
        int travelDays = 0,
        TimeOnly? startTime = null,
        TimeOnly? endTime = null) =>
        new(
            LeaveTypeId.From(Guid.CreateVersion7()),
            "YILLIK",
            deductsFrom,
            start,
            end,
            startPart,
            endPart,
            startTime,
            endTime,
            null,
            travelDays,
            startTime is null ? DurationBasis.WorkingDays : DurationBasis.Hours,
            days,
            days);

    private static LeaveRequest Draft(LeaveRequestPeriod? period = null) =>
        LeaveRequest.Draft(
            "IZN-2026-000001",
            EmployeeId.From(Guid.CreateVersion7()),
            EmploymentId.From(Guid.CreateVersion7()),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            LeaveRequestOrigin.Self,
            period ?? Period(new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 30), 3.5m),
            NoContact);

    private static LeaveRequest Approved(LeaveRequestPeriod? period = null)
    {
        var request = Draft(period);
        request.Submit(requiresApproval: true, Now);
        request.Approve(Now);
        request.ClearDomainEvents();
        return request;
    }

    [Fact]
    public void Approval_TakesTheDaysFromTheBalance()
    {
        var request = Draft();

        request.Submit(requiresApproval: true, Now).Value.ShouldBeTrue();
        request.Status.ShouldBe(LeaveRequestStatus.Pending);
        request.DomainEvents.ShouldBeEmpty();

        request.Approve(Now);

        request.Status.ShouldBe(LeaveRequestStatus.Approved);
        var approved = request.DomainEvents.OfType<LeaveRequestApproved>().ShouldHaveSingleItem();
        approved.ShouldSatisfyAllConditions(e => e.Days.ShouldBe(3.5m), e => e.LeaveTypeCode.ShouldBe("YILLIK"), e => e.Date.ShouldBe(new DateOnly(2026, 10, 26)));
    }

    [Fact]
    public void ATypeWithoutApproval_IsApprovedWhenSent_AndOneWithoutABalanceTakesNothing()
    {
        var request = Draft(Period(new DateOnly(2026, 10, 30), new DateOnly(2026, 11, 1), 3, deductsFrom: null));

        request.Submit(requiresApproval: false, Now).Value.ShouldBeFalse();

        request.Status.ShouldBe(LeaveRequestStatus.Approved);
        request.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void AReturnedRequest_IsCorrectedAndSentAgain()
    {
        var request = Draft();
        request.Submit(requiresApproval: true, Now);

        request.Return("Tarihleri ekip planına göre değiştirin");
        request.Status.ShouldBe(LeaveRequestStatus.Returned);
        request.ReturnComment.ShouldBe("Tarihleri ekip planına göre değiştirin");

        request.Update(Period(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 6), 5), NoContact).IsSuccess.ShouldBeTrue();
        request.Submit(requiresApproval: true, Now).Value.ShouldBeTrue();

        request.ShouldSatisfyAllConditions(r => r.Status.ShouldBe(LeaveRequestStatus.Pending), r => r.ReturnComment.ShouldBeNull(), r => r.Days.ShouldBe(5));
    }

    [Fact]
    public void APendingRequest_CannotBeChanged_ButCanBeCancelled()
    {
        var request = Draft();
        request.Submit(requiresApproval: true, Now);

        request.Update(Period(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 6), 5), NoContact).Error.ShouldBe(LeaveRequestErrors.NotEditable);
        request.Cancel(null, Now).Value.ShouldBeTrue();

        request.Status.ShouldBe(LeaveRequestStatus.Cancelled);
        // The flow's late answer changes nothing
        request.Approve(Now);
        request.Status.ShouldBe(LeaveRequestStatus.Cancelled);
        request.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void ADraft_IsCancelledWithoutAFlowToClose_AndAnApprovedLeaveIsNotCancelledThatWay()
    {
        Draft().Cancel("Vazgeçtim", Now).Value.ShouldBeFalse();
        Approved().Cancel(null, Now).Error.ShouldBe(LeaveRequestErrors.NotCancellable);
    }

    [Fact]
    public void E12_TheApprovedCancellation_GivesTheDaysBack_AndIsAskedOnce()
    {
        var request = Approved();

        request.RequestCancellation(null, Today, Now).Error.ShouldBe(LeaveRequestErrors.ReasonRequired);
        request.RequestCancellation("Plan değişti", Today, Now).IsSuccess.ShouldBeTrue();
        request.Status.ShouldBe(LeaveRequestStatus.CancellationPending);

        request.CompleteCancellation(approved: false, Now);
        request.Status.ShouldBe(LeaveRequestStatus.Approved);
        request.RequestCancellation("Yine de iptal", Today, Now).Error.ShouldBe(LeaveRequestErrors.CancellationAlreadyRequested);
    }

    [Fact]
    public void ACancellationApproved_CancelsTheLeave_AndRaisesTheGiveBack()
    {
        var request = Approved();
        request.RequestCancellation("Plan değişti", Today, Now);

        request.CompleteCancellation(approved: true, Now);

        request.Status.ShouldBe(LeaveRequestStatus.Cancelled);
        request.DomainEvents.OfType<LeaveRequestCancelled>().ShouldHaveSingleItem().LeaveTypeCode.ShouldBe("YILLIK");
    }

    [Fact]
    public void AStartedLeave_IsNotCancelled_ButHrMayEndItEarly()
    {
        var request = Approved();
        var started = new DateOnly(2026, 10, 27);

        request.RequestCancellation("Plan değişti", started, Now).Error.ShouldBe(LeaveRequestErrors.AlreadyStarted);
        request.CancelApproved("Plan değişti", started, Now).Error.ShouldBe(LeaveRequestErrors.AlreadyStarted);

        request.ReturnEarly(Period(new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 27), 2), started).IsSuccess.ShouldBeTrue();

        request.ShouldSatisfyAllConditions(
            r => r.EndDate.ShouldBe(new DateOnly(2026, 10, 27)),
            r => r.OriginalEndDate.ShouldBe(new DateOnly(2026, 10, 30)),
            r => r.Days.ShouldBe(2));
        var returned = request.DomainEvents.OfType<LeaveRequestDaysReturned>().ShouldHaveSingleItem();
        returned.ShouldSatisfyAllConditions(e => e.Days.ShouldBe(1.5m), e => e.Date.ShouldBe(new DateOnly(2026, 10, 28)));
    }

    [Fact]
    public void AnEarlyReturn_NeedsAStartedLeave_AndAnEarlierLastDay()
    {
        var request = Approved();

        request.ReturnEarly(Period(new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 27), 2), Today).Error.ShouldBe(LeaveRequestErrors.NotStarted);
        request.ReturnEarly(Period(new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 30), 3.5m), new DateOnly(2026, 10, 28)).Error
            .ShouldBe(LeaveRequestErrors.InvalidEarlyReturn);
    }

    [Fact]
    public void HrCancelsAnApprovedLeave_ClosingAnOpenCancellationFlow()
    {
        var request = Approved();
        request.RequestCancellation("Plan değişti", Today, Now);

        request.CancelApproved("İK kararı", Today, Now).Value.ShouldBeTrue();

        request.Status.ShouldBe(LeaveRequestStatus.Cancelled);
        request.DomainEvents.OfType<LeaveRequestCancelled>().ShouldHaveSingleItem();
    }

    [Fact]
    public void ARecordedLeave_IsApprovedAtOnce()
    {
        var recorded = LeaveRequest.Record(
            "IZN-2026-000002",
            EmployeeId.From(Guid.CreateVersion7()),
            EmploymentId.From(Guid.CreateVersion7()),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            LeaveRequestOrigin.Record,
            Period(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 6), 5),
            NoContact,
            Now);

        recorded.Status.ShouldBe(LeaveRequestStatus.Approved);
        recorded.DomainEvents.OfType<LeaveRequestApproved>().ShouldHaveSingleItem().Days.ShouldBe(5);
    }

    [Theory]
    [InlineData("2026-10-26", "2026-10-30", "2026-10-30", "2026-11-03", true)]
    [InlineData("2026-10-26", "2026-10-30", "2026-11-02", "2026-11-03", false)]
    [InlineData("2026-10-28", "2026-10-28", "2026-10-26", "2026-10-30", true)]
    public void E13_Overlaps_WhenTheDaysMeet(string start, string end, string otherStart, string otherEnd, bool overlaps)
    {
        var first = Period(DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture), 1);
        var second = Period(DateOnly.Parse(otherStart, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(otherEnd, System.Globalization.CultureInfo.InvariantCulture), 1);

        first.Overlaps(second).ShouldBe(overlaps);
        second.Overlaps(first).ShouldBe(overlaps);
    }

    [Fact]
    public void AMorningAndAnAfternoonOfTheSameDay_DoNotOverlap_ButTravelDaysDo()
    {
        var morning = Period(new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 27), 1.5m, endPart: LeaveDayPart.Morning);
        var afternoon = Period(new DateOnly(2026, 10, 27), new DateOnly(2026, 10, 30), 3.5m, startPart: LeaveDayPart.Afternoon);
        var withTravel = Period(new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 23), 5, travelDays: 3);

        morning.Overlaps(afternoon).ShouldBeFalse();
        afternoon.Overlaps(morning).ShouldBeFalse();
        withTravel.AwayUntil.ShouldBe(new DateOnly(2026, 10, 26));
        withTravel.Overlaps(morning).ShouldBeTrue();
    }

    [Fact]
    public void HourlyLeavesOfTheSameDay_OverlapOnlyWhenTheirHoursDo()
    {
        var day = new DateOnly(2026, 10, 26);
        var morning = Period(day, day, 0.25m, startTime: new TimeOnly(9, 0), endTime: new TimeOnly(11, 0));
        var afternoon = Period(day, day, 0.25m, startTime: new TimeOnly(14, 0), endTime: new TimeOnly(16, 0));
        var noon = Period(day, day, 0.25m, startTime: new TimeOnly(10, 0), endTime: new TimeOnly(12, 0));

        morning.Overlaps(afternoon).ShouldBeFalse();
        morning.Overlaps(noon).ShouldBeTrue();
    }

    [Fact]
    public void AnEarlyReturn_GivesBackAtMostWhatTheRequestTook()
    {
        var stamp = new LedgerStamp(Now, null);
        var account = LeaveAccount.Open(EmployeeId.From(Guid.CreateVersion7()), EmploymentId.From(Guid.CreateVersion7()), Guid.CreateVersion7(), "YILLIK");
        var requestId = Guid.CreateVersion7();
        account.Adjust(new DateOnly(2026, 1, 1), 14, "Devir", stamp);
        account.RecordRequest(new DateOnly(2026, 10, 26), 5, requestId, stamp);

        account.GiveBack(new DateOnly(2026, 10, 29), requestId, 2, stamp).IsSuccess.ShouldBeTrue();
        account.Balance.ShouldBe(11);
        account.Used.ShouldBe(3);

        account.GiveBack(new DateOnly(2026, 10, 29), requestId, 10, stamp).IsSuccess.ShouldBeTrue();
        account.Balance.ShouldBe(14);
        account.GiveBack(new DateOnly(2026, 10, 29), requestId, 1, stamp).Error.ShouldBe(LeaveAccountErrors.NothingToCancel);
    }
}
