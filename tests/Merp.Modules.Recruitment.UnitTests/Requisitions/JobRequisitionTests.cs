using Merp.Modules.Recruitment.Domain.Requisitions;

namespace Merp.Modules.Recruitment.UnitTests.Requisitions;

/// <summary>The life of a requisition (PLAN R2, D48).</summary>
public sealed class JobRequisitionTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    private static RequisitionTerms Terms(Guid? positionId = null, int headcount = 2, RequisitionReason reason = RequisitionReason.NewHeadcount, decimal? salaryMin = null) =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            positionId,
            positionId is null ? "Veri Analisti" : "ignored",
            positionId is null ? "U2" : "ignored",
            headcount,
            reason,
            Guid.CreateVersion7(),
            RequisitionEmploymentType.Indefinite,
            new DateOnly(2027, 1, 4),
            salaryMin,
            salaryMin is null ? null : salaryMin + 10000,
            " try ",
            " Yeni bayi kanalı ",
            Guid.CreateVersion7());

    private static JobRequisition Draft(RequisitionTerms? terms = null) => JobRequisition.Draft("PT-2026-00001", Guid.CreateVersion7(), terms ?? Terms(Guid.CreateVersion7()));

    [Fact]
    public void ADraft_KeepsOnlyWhatItsKindNeeds()
    {
        var existing = Draft(Terms(Guid.CreateVersion7()));
        existing.ShouldSatisfyAllConditions(
            r => r.Status.ShouldBe(RequisitionStatus.Draft),
            r => r.IsNewPosition.ShouldBeFalse(),
            r => r.NewPositionTitle.ShouldBeNull(),
            r => r.ReplacedEmployeeId.ShouldBeNull(),
            r => r.Currency.ShouldBeNull(),
            r => r.Justification.ShouldBe("Yeni bayi kanalı"),
            r => r.ReservesSeats.ShouldBeFalse());

        var proposed = Draft(Terms(reason: RequisitionReason.Replacement, salaryMin: 40000));
        proposed.ShouldSatisfyAllConditions(
            r => r.IsNewPosition.ShouldBeTrue(),
            r => r.NewPositionTitle.ShouldBe("Veri Analisti"),
            r => r.ReplacedEmployeeId.ShouldNotBeNull(),
            r => r.Currency.ShouldBe("TRY"));
    }

    [Fact]
    public void AnInvalidHeadcount_OrANewPositionWithoutATitle_IsRefused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Draft(Terms(Guid.CreateVersion7(), headcount: 0)));
        Should.Throw<ArgumentOutOfRangeException>(() => Draft(Terms(Guid.CreateVersion7(), headcount: JobRequisition.MaxHeadcount + 1)));
        Should.Throw<ArgumentException>(() => Draft(Terms() with { NewPositionTitle = " " }));
    }

    [Fact]
    public void ASubmittedRequisition_HoldsSeats_AndIsApprovedOrRejected()
    {
        var requisition = Draft();
        requisition.Submit(At).IsSuccess.ShouldBeTrue();
        requisition.ShouldSatisfyAllConditions(r => r.Status.ShouldBe(RequisitionStatus.Pending), r => r.SubmittedAt.ShouldBe(At), r => r.ReservesSeats.ShouldBeTrue());
        requisition.Update(Terms(Guid.CreateVersion7())).Error.ShouldBe(JobRequisitionErrors.NotEditable);
        requisition.Submit(At).Error.ShouldBe(JobRequisitionErrors.NotEditable);

        requisition.Complete(approved: true, At.AddDays(1));
        requisition.ShouldSatisfyAllConditions(r => r.Status.ShouldBe(RequisitionStatus.Approved), r => r.DecidedAt.ShouldBe(At.AddDays(1)), r => r.ReservesSeats.ShouldBeTrue());

        // A late or repeated result changes nothing
        requisition.Complete(approved: false, At.AddDays(2));
        requisition.Return("Geç");
        requisition.Status.ShouldBe(RequisitionStatus.Approved);

        var rejected = Draft();
        rejected.Submit(At);
        rejected.Complete(approved: false, At);
        rejected.ShouldSatisfyAllConditions(r => r.Status.ShouldBe(RequisitionStatus.Rejected), r => r.ReservesSeats.ShouldBeFalse());
        rejected.Cancel(null, At).Error.ShouldBe(JobRequisitionErrors.NotCancellable);
    }

    [Fact]
    public void AReturnedRequisition_IsCorrectedAndSentAgain()
    {
        var requisition = Draft();
        requisition.Submit(At);
        requisition.Return("Tek kişiyle başlayalım");
        requisition.ShouldSatisfyAllConditions(r => r.Status.ShouldBe(RequisitionStatus.Returned), r => r.ReturnComment.ShouldBe("Tek kişiyle başlayalım"));

        requisition.Update(Terms(Guid.CreateVersion7(), headcount: 1)).IsSuccess.ShouldBeTrue();
        requisition.Submit(At.AddDays(1)).IsSuccess.ShouldBeTrue();
        requisition.ShouldSatisfyAllConditions(r => r.Headcount.ShouldBe(1), r => r.ReturnComment.ShouldBeNull(), r => r.Status.ShouldBe(RequisitionStatus.Pending));
    }

    [Fact]
    public void Cancelling_SaysWhetherAnApprovalWasOpen()
    {
        var draft = Draft();
        draft.Cancel("Vazgeçildi", At).Value.ShouldBeFalse();
        draft.ShouldSatisfyAllConditions(r => r.Status.ShouldBe(RequisitionStatus.Cancelled), r => r.CancellationReason.ShouldBe("Vazgeçildi"));

        var pending = Draft();
        pending.Submit(At);
        pending.Cancel(null, At).Value.ShouldBeTrue();

        var approved = Draft();
        approved.Submit(At);
        approved.Complete(approved: true, At);
        approved.Cancel("Bütçe donduruldu", At).Value.ShouldBeFalse();
        approved.ReservesSeats.ShouldBeFalse();
    }
}
