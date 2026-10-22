using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.SelfService;

namespace Merp.Modules.HR.UnitTests.SelfService;

/// <summary>A person's change request (D39): approval outcomes, correction after a return, withdrawal.</summary>
public class ProfileChangeRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

    private static ProfileChangeRequest Submit(ProfileChangeKind kind = ProfileChangeKind.Family) =>
        ProfileChangeRequest.Submit(
            "PDT-2026-00001",
            EmployeeId.From(Guid.CreateVersion7()),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            kind,
            kind == ProfileChangeKind.Iban
                ? new ProfileChangeValues("[]", "[]", "TR330006100519786457841326", null)
                : new ProfileChangeValues("""[{"fullName":"Ali Yılmaz"}]""", "[]", "TR330006100519786457841326", null),
            " Doğum ");

    [Fact]
    public void Submit_WaitsForApproval_AndKeepsOnlyTheValuesOfItsKind()
    {
        var family = Submit();
        var iban = Submit(ProfileChangeKind.Iban);

        family.ShouldSatisfyAllConditions(
            r => r.Status.ShouldBe(ProfileChangeStatus.Pending),
            r => r.IsOpen.ShouldBeTrue(),
            r => r.Note.ShouldBe("Doğum"),
            r => r.Before.ShouldBe("[]"),
            r => r.ProposedIban.ShouldBeNull());
        iban.ShouldSatisfyAllConditions(
            r => r.Proposed.ShouldBeNull(),
            r => r.Before.ShouldBeNull(),
            r => r.ProposedIban.ShouldBe("TR330006100519786457841326"));
        Should.Throw<ArgumentException>(() => ProfileChangeRequest.Submit(
            "PDT-2026-00002", EmployeeId.From(Guid.CreateVersion7()), Guid.CreateVersion7(), Guid.CreateVersion7(), ProfileChangeKind.Contacts, new(null, null, null, null), null));
    }

    [Fact]
    public void Close_Applied_AsksForTheCardChange_OtherOutcomesDoNot()
    {
        var applied = Submit();
        var kept = Submit();
        var rejected = Submit();

        applied.Close(ProfileChangeStatus.Applied, Now);
        kept.Close(ProfileChangeStatus.NotApplied, Now);
        rejected.Close(ProfileChangeStatus.Rejected, Now);

        applied.DomainEvents.ShouldBe([new ProfileChangeApproved(applied.Id, applied.EmployeeId, ProfileChangeKind.Family, "PDT-2026-00001", applied.Proposed, null)]);
        applied.ClosedAt.ShouldBe(Now);
        applied.IsOpen.ShouldBeFalse();
        kept.Status.ShouldBe(ProfileChangeStatus.NotApplied);
        kept.DomainEvents.ShouldBeEmpty();
        rejected.DomainEvents.ShouldBeEmpty();
        Should.Throw<ArgumentOutOfRangeException>(() => Submit().Close(ProfileChangeStatus.Withdrawn, Now));
    }

    [Fact]
    public void Close_IsIgnored_OnceTheRequestIsClosed()
    {
        var request = Submit();
        request.Withdraw(Now).IsSuccess.ShouldBeTrue();

        request.Close(ProfileChangeStatus.Applied, Now.AddHours(1));

        request.Status.ShouldBe(ProfileChangeStatus.Withdrawn);
        request.ClosedAt.ShouldBe(Now);
        request.DomainEvents.ShouldBeEmpty();
        request.Withdraw(Now).Error.ShouldBe(ProfileChangeErrors.Closed);
    }

    [Fact]
    public void AReturnedRequest_IsCorrectedAndWaitsAgain_OnlyAReturnedOneCanBeResubmitted()
    {
        var request = Submit();
        request.Resubmit(new("[]", "[]", null, null), null).Error.ShouldBe(ProfileChangeErrors.NotReturned);

        request.Return("Nüfus kaydını ekleyin");
        request.ShouldSatisfyAllConditions(
            r => r.Status.ShouldBe(ProfileChangeStatus.Returned),
            r => r.ReturnComment.ShouldBe("Nüfus kaydını ekleyin"),
            r => r.IsOpen.ShouldBeTrue());

        request.Resubmit(new("""[{"fullName":"Ali Can Yılmaz"}]""", "[]", null, null), "Düzelttim").IsSuccess.ShouldBeTrue();
        request.ShouldSatisfyAllConditions(
            r => r.Status.ShouldBe(ProfileChangeStatus.Pending),
            r => r.Proposed.ShouldBe("""[{"fullName":"Ali Can Yılmaz"}]"""),
            r => r.Note.ShouldBe("Düzelttim"),
            r => r.ReturnComment.ShouldBeNull());
    }

    [Fact]
    public void Return_IsIgnored_OnceTheRequestIsClosed()
    {
        var request = Submit();
        request.Close(ProfileChangeStatus.Rejected, Now);

        request.Return("Geç geldi");

        request.Status.ShouldBe(ProfileChangeStatus.Rejected);
        request.ReturnComment.ShouldBeNull();
    }
}
