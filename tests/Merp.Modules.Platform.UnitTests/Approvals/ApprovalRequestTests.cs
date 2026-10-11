using Merp.Modules.Platform.Application.Approvals;
using Merp.Modules.Platform.Domain.Approvals;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Platform.UnitTests.Approvals;

/// <summary>States, decisions and skip rules of approval requests (docs/domain/yetki-ve-onay.md §2.5, §2.6).</summary>
public sealed class ApprovalRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Requester = Guid.CreateVersion7();
    private static readonly Guid A = Guid.CreateVersion7();
    private static readonly Guid B = Guid.CreateVersion7();
    private static readonly Guid C = Guid.CreateVersion7();

    [Fact]
    public void AnyMode_FirstApprovalCompletesTheStep()
    {
        var request = Submit(Step(ApprovalMode.Any));
        var tasks = request.OpenStep(0, [A, B], null, Now);

        request.Decide(tasks[0].Id, A, ApprovalDecision.Approve, null, Now).Value.ShouldBe(DecisionEffect.StepCompleted);

        tasks[1].Status.ShouldBe(ApprovalTaskStatus.Closed);
        request.ApprovedInRound().ShouldBe([A]);
    }

    [Fact]
    public void AllMode_WaitsForEveryone_AndOneRejectionRejects()
    {
        var request = Submit(Step(ApprovalMode.All));
        var tasks = request.OpenStep(0, [A, B, C], null, Now);

        request.Decide(tasks[0].Id, A, ApprovalDecision.Approve, null, Now).Value.ShouldBe(DecisionEffect.StepPending);
        request.Decide(tasks[1].Id, B, ApprovalDecision.Approve, null, Now).Value.ShouldBe(DecisionEffect.StepPending);
        request.Decide(tasks[2].Id, C, ApprovalDecision.Reject, "Hayır", Now).Value.ShouldBe(DecisionEffect.Rejected);

        request.Status.ShouldBe(ApprovalStatus.Rejected);
        request.CompletedAt.ShouldBe(Now);
    }

    [Fact]
    public void RejectAndReturn_NeedAComment_AndReturnNeedsTheStepOption()
    {
        var request = Submit(Step(allowReturn: false));
        var task = request.OpenStep(0, [A], null, Now)[0];

        request.Decide(task.Id, A, ApprovalDecision.Reject, "  ", Now).Error.Fields.Single().Code.ShouldBe(ApprovalRequest.CommentRequiredCode);
        request.Decide(task.Id, A, ApprovalDecision.Return, null, Now).Error.Fields.Single().Code.ShouldBe(ApprovalRequest.CommentRequiredCode);
        request.Decide(task.Id, A, ApprovalDecision.Return, "Düzelt", Now).Error.ShouldBe(ApprovalRequest.ReturnNotAllowed);
        request.Decide(task.Id, A, ApprovalDecision.Approve, new string('x', ApprovalRequest.CommentMaxLength + 1), Now).Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void DecidedTask_CannotBeDecidedAgain()
    {
        var request = Submit(Step(), Step());
        var task = request.OpenStep(0, [A], null, Now)[0];
        request.Decide(task.Id, A, ApprovalDecision.Approve, null, Now);

        request.Decide(task.Id, A, ApprovalDecision.Approve, null, Now).Error.ShouldBe(ApprovalRequest.TaskNotOpen);
    }

    [Fact]
    public void Delegate_DecidesOnBehalfOfTheAssignee()
    {
        var request = Submit(Step());
        var task = request.OpenStep(0, [A], null, Now)[0];

        request.Decide(task.Id, B, ApprovalDecision.Approve, null, Now);

        task.DecidedByUserId.ShouldBe(B);
        task.OnBehalfOfUserId.ShouldBe(A);
        request.History.Last().OnBehalfOfUserId.ShouldBe(A);
    }

    [Fact]
    public void Returned_IsResubmittedAsANewRound_KeepingTheHistory()
    {
        var request = Submit(Step(allowReturn: true));
        var task = request.OpenStep(0, [A], null, Now)[0];

        request.Resubmit(Content("{}"), Now).Error.ShouldBe(ApprovalRequest.NotPending);
        request.Decide(task.Id, A, ApprovalDecision.Return, "Eksik", Now).Value.ShouldBe(DecisionEffect.Returned);
        request.Status.ShouldBe(ApprovalStatus.Returned);
        request.Resubmit(Content("""{"days":3}"""), Now.AddHours(1)).IsSuccess.ShouldBeTrue();

        request.Round.ShouldBe(2);
        request.CurrentStep.ShouldBeNull();
        request.NextStep.ShouldBe(0);
        request.Facts.ShouldBe("""{"days":3}""");
        request.ApprovedInRound().ShouldBeEmpty();
        request.History.Select(h => h.Kind).ShouldBe([ApprovalEventKind.Submitted, ApprovalEventKind.Assigned, ApprovalEventKind.Returned, ApprovalEventKind.Resubmitted]);
        request.History.Select(h => h.Sequence).ShouldBe([1, 2, 3, 4]);
    }

    [Fact]
    public void Reassign_MovesTheTask_WithTheSameTimeToDecide()
    {
        var request = Submit(Step());
        var task = request.OpenStep(0, [A], Now.AddHours(8), Now)[0];

        var moved = request.Reassign(task.Id, B, actor: null, escalation: true, Now.AddHours(9)).Value;

        task.Status.ShouldBe(ApprovalTaskStatus.Reassigned);
        moved.AssigneeUserId.ShouldBe(B);
        moved.DueAt.ShouldBe(Now.AddHours(17));
        request.OpenTasks.ShouldBe([moved]);
        request.History.ShouldContain(h => h.Kind == ApprovalEventKind.Escalated && h.OnBehalfOfUserId == A);
        request.Decide(task.Id, A, ApprovalDecision.Approve, null, Now).Error.ShouldBe(ApprovalRequest.TaskNotOpen);
        request.Decide(moved.Id, B, ApprovalDecision.Approve, null, Now).Value.ShouldBe(DecisionEffect.StepCompleted);
    }

    [Fact]
    public void OverdueStep_IsDecidedByTheSystem()
    {
        var approved = Submit(Step());
        approved.OpenStep(0, [A], Now, Now);
        approved.DecideOverdue(ApprovalDecision.Approve, Now).Value.ShouldBe(DecisionEffect.StepCompleted);
        approved.History.Last().ShouldSatisfyAllConditions(h => h.Kind.ShouldBe(ApprovalEventKind.Approved), h => h.UserId.ShouldBeNull());

        var rejected = Submit(Step());
        rejected.OpenStep(0, [A], Now, Now);
        rejected.DecideOverdue(ApprovalDecision.Reject, Now).Value.ShouldBe(DecisionEffect.Rejected);
        rejected.Status.ShouldBe(ApprovalStatus.Rejected);
        rejected.DecideOverdue(ApprovalDecision.Approve, Now).Error.ShouldBe(ApprovalRequest.NotPending);
    }

    [Fact]
    public void NeedsAttention_IsLeftByAssigningOrForceApproving()
    {
        var request = Submit(Step(), Step());
        request.NeedAttention(0, ApprovalEngine.Reasons.NoApprover, Now);
        request.Status.ShouldBe(ApprovalStatus.NeedsAttention);

        request.ForceApprove(C, "Acil", Now).IsSuccess.ShouldBeTrue();
        request.OpenStep(1, [A], null, Now);

        request.Status.ShouldBe(ApprovalStatus.Pending);
        request.History.ShouldContain(h => h.Kind == ApprovalEventKind.ForceApproved && h.UserId == C && h.Detail == "Acil");
    }

    [Fact]
    public void Cancel_ClosesOpenTasks_AndEndedRequestsStay()
    {
        var request = Submit(Step());
        var task = request.OpenStep(0, [A], null, Now)[0];

        request.Cancel(Now).IsSuccess.ShouldBeTrue();

        task.Status.ShouldBe(ApprovalTaskStatus.Closed);
        request.Status.ShouldBe(ApprovalStatus.Cancelled);
        request.Cancel(Now).Error.ShouldBe(ApprovalRequest.NotPending);
        request.ForceApprove(C, null, Now).Error.ShouldBe(ApprovalRequest.NotPending);
    }

    [Fact]
    public void EveryChange_MovesTheLastActivity()
    {
        var request = Submit(Step());
        request.LastActivityAt.ShouldBe(Now);
        var task = request.OpenStep(0, [A, B], null, Now.AddMinutes(1))[0];

        request.Decide(task.Id, A, ApprovalDecision.Approve, null, Now.AddMinutes(2));

        request.LastActivityAt.ShouldBe(Now.AddMinutes(2));
    }

    [Theory]
    [InlineData(ApprovalMode.Any)]
    [InlineData(ApprovalMode.All)]
    public void Choose_DropsTheRequester(ApprovalMode mode)
    {
        var request = Submit(Step(mode));

        var others = ApprovalEngine.Choose(request, request.Steps[0], [Requester, A]);
        others.Approvers.ShouldBe([A]);
        others.Skip.ShouldBeNull();
        ApprovalEngine.Choose(request, request.Steps[0], [Requester]).Skip.ShouldBe(ApprovalEngine.Reasons.Requester);
        ApprovalEngine.Choose(request, request.Steps[0] with { SkipIfApproverIsRequester = false }, [Requester]).Approvers.ShouldBe([Requester]);

        // Nobody found is not a skip: the fallback is tried
        var nobody = ApprovalEngine.Choose(request, request.Steps[0], []);
        nobody.Approvers.ShouldBeEmpty();
        nobody.Skip.ShouldBeNull();
    }

    [Fact]
    public void Choose_SkipsWhatEarlierApproversDecided()
    {
        var request = Submit(Step(), Step(ApprovalMode.Any), Step(ApprovalMode.All));
        var task = request.OpenStep(0, [A], null, Now)[0];
        request.Decide(task.Id, A, ApprovalDecision.Approve, null, Now);

        ApprovalEngine.Choose(request, request.Steps[1], [A, B]).Skip.ShouldBe(ApprovalEngine.Reasons.AlreadyApproved);
        ApprovalEngine.Choose(request, request.Steps[2], [A, B]).Approvers.ShouldBe([B]);
        ApprovalEngine.Choose(request, request.Steps[2], [A]).Skip.ShouldBe(ApprovalEngine.Reasons.AlreadyApproved);
        ApprovalEngine.Choose(request, request.Steps[2] with { SkipIfAlreadyApproved = false }, [A]).Approvers.ShouldBe([A]);
    }

    [Fact]
    public void Delegation_CoversItsTypesAndDays_UntilRevoked()
    {
        var day = new DateOnly(2026, 3, 2);
        Delegation.Create(A, A, day, day, []).Error.ShouldBe(Delegation.SelfDelegation);
        Delegation.Create(A, B, day, day.AddDays(-1), []).Error.Fields.Single().Field.ShouldBe("to");

        var all = Delegation.Create(A, B, day, day.AddDays(2), []).Value;
        var leave = Delegation.Create(A, B, day, day, ["hr.leave-request", "hr.leave-request"]).Value;

        all.Covers("recruitment.offer", day.AddDays(2)).ShouldBeTrue();
        all.Covers("recruitment.offer", day.AddDays(3)).ShouldBeFalse();
        leave.DocumentTypes.ShouldBe(["hr.leave-request"]);
        leave.Covers("recruitment.offer", day).ShouldBeFalse();
        all.Revoke();
        all.Covers("recruitment.offer", day).ShouldBeFalse();
    }

    [Fact]
    public void Definition_ChangesCountRevisions()
    {
        var definition = ApprovalDefinition.Create("hr.leave-request", null, LocalizedString.Of("tr-TR", "İzin"), 0, null, [Step()], null);
        definition.Revision.ShouldBe(1);

        definition.Change(definition.Name, 1, " ", [Step(), Step()], null, isActive: false);

        definition.Revision.ShouldBe(2);
        definition.EntryCondition.ShouldBeNull();
        definition.IsActive.ShouldBeFalse();
        Should.Throw<ArgumentException>(() => definition.Change(definition.Name, 1, null, [], null, isActive: true));
    }

    private static ApprovalRequest Submit(params ApprovalStep[] steps)
    {
        var definition = ApprovalDefinition.Create("hr.leave-request", null, LocalizedString.Of("tr-TR", "İzin"), 0, null, steps, null);
        return ApprovalRequest.Submit("hr.leave-request", "42", null, Requester, null, Content("{}"), definition, Now);
    }

    private static ApprovalContent Content(string facts) => new(facts, "hr.leave.title", new Dictionary<string, string>(), null, null);

    private static ApprovalStep Step(ApprovalMode mode = ApprovalMode.Any, bool allowReturn = false) =>
        new(new Dictionary<string, string> { ["tr-TR"] = "Adım" }, new ApproverRule("user", new Dictionary<string, string>()), mode, AllowReturn: allowReturn);
}
