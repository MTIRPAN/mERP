using Merp.Application.Persistence;
using Merp.Modules.Platform.Application.Approvals;
using Merp.Modules.Platform.Application.Notifications;
using Merp.Modules.Platform.Contracts.Approvals;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Platform.Domain.Approvals;
using Merp.SharedKernel.Results;
using static Merp.Modules.Platform.IntegrationTests.Approvals.ApprovalFactory;

namespace Merp.Modules.Platform.IntegrationTests.Approvals;

/// <summary>The mandatory scenarios of docs/domain/yetki-ve-onay.md §2.9 (1–10) and the flows around them.</summary>
public sealed class ApprovalEngineTests(ApprovalFactory factory) : IClassFixture<ApprovalFactory>
{
    [Fact]
    public async Task Scenario1_company_definition_overrides_the_general_one()
    {
        var (company, other) = (await factory.CompanyAsync(), await factory.CompanyAsync());
        var (general, own, requester) = (await factory.UserAsync(), await factory.UserAsync(), await factory.UserAsync());
        await factory.DefineAsync(null, [Step(User(general))], Purchase, priority: -100);
        await factory.DefineAsync(company, [Step(User(own))], Purchase, priority: 100);

        var mine = await factory.RequestAsync(await factory.SubmitAsync(company, requester, new { amount = 10 }, Purchase));
        var theirs = await factory.RequestAsync(await factory.SubmitAsync(other, requester, new { amount = 10 }, Purchase));

        mine.OpenTasks.Single().AssigneeUserId.ShouldBe(own);
        theirs.OpenTasks.Single().AssigneeUserId.ShouldBe(general);
    }

    [Fact]
    public async Task Entry_condition_and_priority_choose_among_definitions_of_a_company()
    {
        var company = await factory.CompanyAsync();
        var (large, small, requester) = (await factory.UserAsync(), await factory.UserAsync(), await factory.UserAsync());
        await factory.DefineAsync(company, [Step(User(large))], priority: 1, entryCondition: """{ "fact": "amount", "op": "gt", "value": 1000 }""");
        await factory.DefineAsync(company, [Step(User(small))], priority: 2);

        (await factory.RequestAsync(await factory.SubmitAsync(company, requester, new { amount = 5000 }))).OpenTasks.Single().AssigneeUserId.ShouldBe(large);
        (await factory.RequestAsync(await factory.SubmitAsync(company, requester, new { amount = 50 }))).OpenTasks.Single().AssigneeUserId.ShouldBe(small);
    }

    [Fact]
    public async Task Scenario2_step_whose_condition_fails_is_skipped_and_recorded()
    {
        var company = await factory.CompanyAsync();
        var (first, second, requester) = (await factory.UserAsync(), await factory.UserAsync(), await factory.UserAsync());
        await factory.DefineAsync(company, [
            Step(User(first), condition: """{ "any": [ { "fact": "amount", "op": "gt", "value": 5 }, { "fact": "category", "op": "eq", "value": "UCRETSIZ" } ] }"""),
            Step(User(second)),
        ]);

        var request = await factory.RequestAsync(await factory.SubmitAsync(company, requester, new { amount = 3, category = "YILLIK" }));

        request.CurrentStep.ShouldBe(1);
        request.OpenTasks.Single().AssigneeUserId.ShouldBe(second);
        request.History.ShouldContain(h => h.Kind == ApprovalEventKind.Skipped && h.Step == 0 && h.Detail == ApprovalEngine.Reasons.Condition);
    }

    [Fact]
    public async Task Scenario3_requester_who_is_their_own_approver_skips_the_step()
    {
        var company = await factory.CompanyAsync();
        var (next, requester) = (await factory.UserAsync(), await factory.UserAsync());
        await factory.DefineAsync(company, [Step(User(requester)), Step(User(next))]);

        var request = await factory.RequestAsync(await factory.SubmitAsync(company, requester, new { amount = 1 }));

        request.OpenTasks.Single().AssigneeUserId.ShouldBe(next);
        request.History.ShouldContain(h => h.Kind == ApprovalEventKind.Skipped && h.Step == 0 && h.Detail == ApprovalEngine.Reasons.Requester);
    }

    [Fact]
    public async Task Scenario3_step_without_approver_goes_to_the_fallback()
    {
        var company = await factory.CompanyAsync();
        var (fallback, requester) = (await factory.UserAsync(), await factory.UserAsync());

        // The top manager has no manager: the fact is empty and the resolver finds nobody
        await factory.DefineAsync(company, [Step(FactUser("approverUserId"))], fallback: User(fallback));

        var request = await factory.RequestAsync(await factory.SubmitAsync(company, requester, new { amount = 1 }));

        request.Status.ShouldBe(ApprovalStatus.Pending);
        request.OpenTasks.Single().AssigneeUserId.ShouldBe(fallback);
    }

    [Fact]
    public async Task Fact_user_and_role_resolvers_find_their_approvers()
    {
        var company = await factory.CompanyAsync();
        var (named, inCompany, inOtherCompany, tenantWide, requester) =
            (await factory.UserAsync(), await factory.UserAsync(), await factory.UserAsync(), await factory.UserAsync(), await factory.UserAsync());
        var role = await factory.RoleAsync();
        await factory.AssignAsync(inCompany, role, "Company", company);
        await factory.AssignAsync(inOtherCompany, role, "Company", await factory.CompanyAsync());
        await factory.AssignAsync(tenantWide, role);
        await factory.DefineAsync(company, [Step(FactUser("approverUserId")), Step(Role(role))]);

        var id = await factory.SubmitAsync(company, requester, new { amount = 1, approverUserId = named });
        (await factory.DecideAsync(await factory.TaskOfAsync(id, named), named, ApprovalDecision.Approve)).IsSuccess.ShouldBeTrue();

        (await factory.RequestAsync(id)).OpenTasks.Select(t => t.AssigneeUserId).ShouldBe([inCompany, tenantWide], ignoreOrder: true);
    }

    [Fact]
    public async Task Scenario4_one_rejection_in_all_mode_rejects_the_request()
    {
        var company = await factory.CompanyAsync();
        var (a, b, c, requester) = (await factory.UserAsync(), await factory.UserAsync(), await factory.UserAsync(), await factory.UserAsync());
        var role = await factory.RoleAsync();
        foreach (var user in new[] { a, b, c })
        {
            await factory.AssignAsync(user, role, "Company", company);
        }

        await factory.DefineAsync(company, [Step(Role(role), ApprovalMode.All)]);
        var id = await factory.SubmitAsync(company, requester, new { amount = 1 });

        (await factory.DecideAsync(await factory.TaskOfAsync(id, a), a, ApprovalDecision.Approve)).IsSuccess.ShouldBeTrue();
        (await factory.RequestAsync(id)).Status.ShouldBe(ApprovalStatus.Pending);
        (await factory.DecideAsync(await factory.TaskOfAsync(id, b), b, ApprovalDecision.Approve)).IsSuccess.ShouldBeTrue();
        (await factory.DecideAsync(await factory.TaskOfAsync(id, c), c, ApprovalDecision.Reject)).Error.Fields.Single().Code.ShouldBe(ApprovalRequest.CommentRequiredCode);
        (await factory.DecideAsync(await factory.TaskOfAsync(id, c), c, ApprovalDecision.Reject, "Bütçe yok")).IsSuccess.ShouldBeTrue();

        var request = await factory.RequestAsync(id);
        request.Status.ShouldBe(ApprovalStatus.Rejected);
        request.OpenTasks.ShouldBeEmpty();
        (await factory.OutboxAsync<ApprovalRequestCompleted>()).ShouldContain(e => e.DocumentId == id && e.Outcome == ApprovalOutcome.Rejected);
    }

    [Fact]
    public async Task All_mode_completes_when_everyone_approves()
    {
        var company = await factory.CompanyAsync();
        var (a, b, requester) = (await factory.UserAsync(), await factory.UserAsync(), await factory.UserAsync());
        var role = await factory.RoleAsync();
        await factory.AssignAsync(a, role, "Company", company);
        await factory.AssignAsync(b, role, "Company", company);
        await factory.DefineAsync(company, [Step(Role(role), ApprovalMode.All)]);
        var id = await factory.SubmitAsync(company, requester, new { amount = 1 });

        await factory.DecideAsync(await factory.TaskOfAsync(id, a), a, ApprovalDecision.Approve);
        await factory.DecideAsync(await factory.TaskOfAsync(id, b), b, ApprovalDecision.Approve);

        (await factory.RequestAsync(id)).Status.ShouldBe(ApprovalStatus.Approved);
        (await factory.OutboxAsync<ApprovalRequestCompleted>()).ShouldContain(e => e.DocumentId == id && e.Outcome == ApprovalOutcome.Approved);
    }

    [Fact]
    public async Task Approver_of_an_earlier_step_is_not_asked_again()
    {
        var company = await factory.CompanyAsync();
        var (a, b, requester) = (await factory.UserAsync(), await factory.UserAsync(), await factory.UserAsync());
        var role = await factory.RoleAsync();
        await factory.AssignAsync(a, role, "Company", company);
        await factory.AssignAsync(b, role, "Company", company);
        await factory.DefineAsync(company, [Step(User(a)), Step(Role(role))]);
        var id = await factory.SubmitAsync(company, requester, new { amount = 1 });

        await factory.DecideAsync(await factory.TaskOfAsync(id, a), a, ApprovalDecision.Approve);

        var request = await factory.RequestAsync(id);
        request.Status.ShouldBe(ApprovalStatus.Approved);
        request.History.ShouldContain(h => h.Kind == ApprovalEventKind.Skipped && h.Step == 1 && h.Detail == ApprovalEngine.Reasons.AlreadyApproved);
    }

    [Fact]
    public async Task Scenario5_delegate_approves_on_behalf_of_the_approver()
    {
        var company = await factory.CompanyAsync();
        var (approver, @delegate, requester) = (await factory.UserAsync(), await factory.UserAsync(), await factory.UserAsync());
        var today = DateOnly.FromDateTime(factory.Time.GetUtcNow().UtcDateTime);
        (await factory.SendAsync(new CreateDelegationCommand(@delegate, today.AddDays(-1), today.AddDays(7), [Expense]), approver)).IsSuccess.ShouldBeTrue();
        await factory.DefineAsync(company, [Step(User(approver))]);
        var id = await factory.SubmitAsync(company, requester, new { amount = 1 });
        var task = await factory.TaskOfAsync(id, approver);

        (await factory.OutboxAsync<NotificationRequested>()).ShouldContain(n =>
            n.Type == PlatformNotificationTypes.ApprovalAssigned && n.RecipientUserIds.Contains(approver) && n.RecipientUserIds.Contains(@delegate));
        (await factory.DecideAsync(task, await factory.UserAsync(), ApprovalDecision.Approve)).Error.ShouldBe(CommonErrors.NotFound);
        (await factory.DecideAsync(task, @delegate, ApprovalDecision.Approve, "Vekaleten")).IsSuccess.ShouldBeTrue();

        var request = await factory.RequestAsync(id);
        request.Status.ShouldBe(ApprovalStatus.Approved);
        var decided = request.Tasks.Single(t => t.Id.Value == task);
        decided.DecidedByUserId.ShouldBe(@delegate);
        decided.OnBehalfOfUserId.ShouldBe(approver);
        request.History.ShouldContain(h => h.Kind == ApprovalEventKind.Approved && h.UserId == @delegate && h.OnBehalfOfUserId == approver);
    }

    [Fact]
    public async Task Delegate_cannot_approve_their_own_request()
    {
        var company = await factory.CompanyAsync();
        var (approver, @delegate) = (await factory.UserAsync(), await factory.UserAsync());
        var today = DateOnly.FromDateTime(factory.Time.GetUtcNow().UtcDateTime);
        await factory.SendAsync(new CreateDelegationCommand(@delegate, today, today, []), approver);
        await factory.DefineAsync(company, [Step(User(approver))]);
        var id = await factory.SubmitAsync(company, @delegate, new { amount = 1 });

        (await factory.DecideAsync(await factory.TaskOfAsync(id, approver), @delegate, ApprovalDecision.Approve)).Error.ShouldBe(ApprovalErrors.OwnRequest);
    }

    [Fact]
    public async Task Revoked_or_expired_delegation_gives_no_access()
    {
        var company = await factory.CompanyAsync();
        var (approver, revoked, expired, requester) = (await factory.UserAsync(), await factory.UserAsync(), await factory.UserAsync(), await factory.UserAsync());
        var today = DateOnly.FromDateTime(factory.Time.GetUtcNow().UtcDateTime);
        var delegation = await factory.SendAsync(new CreateDelegationCommand(revoked, today, today.AddDays(3), []), approver);
        (await factory.SendAsync(new RevokeDelegationCommand { Id = delegation.Value }, revoked)).Error.ShouldBe(CommonErrors.NotFound);
        (await factory.SendAsync(new RevokeDelegationCommand { Id = delegation.Value }, approver)).IsSuccess.ShouldBeTrue();
        await factory.SendAsync(new CreateDelegationCommand(expired, today.AddDays(-5), today.AddDays(-1), []), approver);
        await factory.DefineAsync(company, [Step(User(approver))]);
        var task = await factory.TaskOfAsync(await factory.SubmitAsync(company, requester, new { amount = 1 }), approver);

        (await factory.DecideAsync(task, revoked, ApprovalDecision.Approve)).Error.ShouldBe(CommonErrors.NotFound);
        (await factory.DecideAsync(task, expired, ApprovalDecision.Approve)).Error.ShouldBe(CommonErrors.NotFound);
    }

    [Fact]
    public async Task Scenario6_overdue_task_moves_to_the_approvers_manager()
    {
        var company = await factory.CompanyAsync();
        var (approver, manager, requester) = (await factory.UserAsync(), await factory.UserAsync(), await factory.UserAsync());
        factory.Managers.Managers[approver] = manager;
        await factory.DefineAsync(company, [Step(User(approver), dueHours: 4, escalation: ApprovalEscalation.EscalateToManager)]);
        var id = await factory.SubmitAsync(company, requester, new { amount = 1 });
        var requestId = (await factory.RequestAsync(id)).Id.Value;

        (await factory.SendAsync(new GetOverdueApprovalRequestsQuery())).Value.ShouldNotContain(requestId);
        factory.Time.Advance(TimeSpan.FromHours(5));
        (await factory.SendAsync(new GetOverdueApprovalRequestsQuery())).Value.ShouldContain(requestId);
        (await factory.SendAsync(new EscalateApprovalCommand(requestId))).IsSuccess.ShouldBeTrue();

        var request = await factory.RequestAsync(id);
        var moved = request.OpenTasks.Single();
        moved.AssigneeUserId.ShouldBe(manager);
        moved.DueAt.ShouldBe(factory.Time.GetUtcNow().AddHours(4));
        request.Tasks.ShouldContain(t => t.AssigneeUserId == approver && t.Status == ApprovalTaskStatus.Reassigned);
        request.History.ShouldContain(h => h.Kind == ApprovalEventKind.Escalated && h.OnBehalfOfUserId == approver && h.Detail == manager.ToString());
        (await factory.SendAsync(new GetOverdueApprovalRequestsQuery())).Value.ShouldNotContain(requestId);
    }

    [Fact]
    public async Task Overdue_task_without_a_manager_is_reminded_once()
    {
        var company = await factory.CompanyAsync();
        var (approver, requester) = (await factory.UserAsync(), await factory.UserAsync());
        await factory.DefineAsync(company, [Step(User(approver), dueHours: 1, escalation: ApprovalEscalation.EscalateToManager)]);
        var id = await factory.SubmitAsync(company, requester, new { amount = 1 });
        var requestId = (await factory.RequestAsync(id)).Id.Value;

        factory.Time.Advance(TimeSpan.FromHours(2));
        await factory.SendAsync(new EscalateApprovalCommand(requestId));

        var request = await factory.RequestAsync(id);
        request.OpenTasks.Single().AssigneeUserId.ShouldBe(approver);
        request.History.Count(h => h.Kind == ApprovalEventKind.Reminded).ShouldBe(1);
        (await factory.OutboxAsync<NotificationRequested>()).ShouldContain(n => n.Type == PlatformNotificationTypes.ApprovalOverdue && n.RecipientUserIds.Contains(approver));
        (await factory.SendAsync(new GetOverdueApprovalRequestsQuery())).Value.ShouldNotContain(requestId);
    }

    [Fact]
    public async Task Overdue_step_is_approved_or_rejected_by_the_system()
    {
        var company = await factory.CompanyAsync();
        var (approver, requester) = (await factory.UserAsync(), await factory.UserAsync());
        await factory.DefineAsync(company, [Step(User(approver), dueHours: 1, escalation: ApprovalEscalation.AutoApprove)], priority: 1, entryCondition: """{ "fact": "category", "op": "eq", "value": "A" }""");
        await factory.DefineAsync(company, [Step(User(approver), dueHours: 1, escalation: ApprovalEscalation.AutoReject)], priority: 2);
        var approved = await factory.SubmitAsync(company, requester, new { category = "A" });
        var rejected = await factory.SubmitAsync(company, requester, new { category = "R" });

        factory.Time.Advance(TimeSpan.FromHours(2));
        await factory.SendAsync(new EscalateApprovalCommand((await factory.RequestAsync(approved)).Id.Value));
        await factory.SendAsync(new EscalateApprovalCommand((await factory.RequestAsync(rejected)).Id.Value));

        (await factory.RequestAsync(approved)).Status.ShouldBe(ApprovalStatus.Approved);
        (await factory.RequestAsync(rejected)).Status.ShouldBe(ApprovalStatus.Rejected);
        (await factory.OutboxAsync<ApprovalRequestCompleted>()).ShouldContain(e => e.DocumentId == rejected && e.Outcome == ApprovalOutcome.Rejected && e.Reason == ApprovalRequest.OverdueReason);
    }

    [Fact]
    public async Task Scenario7_returned_and_resubmitted_request_starts_at_step_one_and_keeps_the_history()
    {
        var company = await factory.CompanyAsync();
        var (first, second, requester) = (await factory.UserAsync(), await factory.UserAsync(), await factory.UserAsync());
        await factory.DefineAsync(company, [Step(User(first)), Step(User(second), allowReturn: true)]);
        var id = await factory.SubmitAsync(company, requester, new { amount = 1 });
        (await factory.DecideAsync(await factory.TaskOfAsync(id, first), first, ApprovalDecision.Return, "Olmaz")).Error.ShouldBe(ApprovalRequest.ReturnNotAllowed);
        await factory.DecideAsync(await factory.TaskOfAsync(id, first), first, ApprovalDecision.Approve);

        (await factory.DecideAsync(await factory.TaskOfAsync(id, second), second, ApprovalDecision.Return, "Fatura eksik")).IsSuccess.ShouldBeTrue();
        (await factory.RequestAsync(id)).Status.ShouldBe(ApprovalStatus.Returned);
        (await factory.OutboxAsync<ApprovalRequestReturned>()).ShouldContain(e => e.DocumentId == id && e.ReturnedByUserId == second && e.Comment == "Fatura eksik");

        await factory.SubmitAsync(company, requester, new { amount = 2 }, documentId: id);

        var request = await factory.RequestAsync(id);
        request.Status.ShouldBe(ApprovalStatus.Pending);
        request.Round.ShouldBe(2);
        request.CurrentStep.ShouldBe(0);
        request.OpenTasks.Single().AssigneeUserId.ShouldBe(first);
        request.History.ShouldContain(h => h.Round == 1 && h.Kind == ApprovalEventKind.Returned && h.Detail == "Fatura eksik");
        request.History.ShouldContain(h => h.Round == 2 && h.Kind == ApprovalEventKind.Resubmitted);
        request.Facts.ShouldContain("2");
    }

    [Fact]
    public async Task Scenario8_running_request_keeps_the_steps_it_copied()
    {
        var company = await factory.CompanyAsync();
        var (before, after, requester) = (await factory.UserAsync(), await factory.UserAsync(), await factory.UserAsync());
        var definition = await factory.DefineAsync(company, [Step(User(before))]);
        var id = await factory.SubmitAsync(company, requester, new { amount = 1 });
        var version = await factory.QueryAsync(db => Task.FromResult(db.Entry(db.ApprovalDefinitions.Single(d => d.Id == ApprovalDefinitionId.From(definition)))
            .Property<uint>(ConcurrencyVersion.PropertyName).CurrentValue));

        (await factory.SendAsync(
            new UpdateApprovalDefinitionCommand(version, Name("Yeni akış"), 0, null, [Step(User(after)), Step(User(after))], null, IsActive: true) { Id = definition },
            factory.RootId)).IsSuccess.ShouldBeTrue();
        await factory.DecideAsync(await factory.TaskOfAsync(id, before), before, ApprovalDecision.Approve);

        var request = await factory.RequestAsync(id);
        request.Status.ShouldBe(ApprovalStatus.Approved);
        request.DefinitionRevision.ShouldBe(1);
        (await factory.RequestAsync(await factory.SubmitAsync(company, requester, new { amount = 1 }))).OpenTasks.Single().AssigneeUserId.ShouldBe(after);
    }

    [Fact]
    public async Task Scenario9_no_approver_and_no_fallback_needs_attention_and_notifies_administrators()
    {
        var company = await factory.CompanyAsync();
        var (approver, requester) = (await factory.UserAsync(), await factory.UserAsync());
        await factory.DefineAsync(company, [Step(FactUser("approverUserId"))]);
        var id = await factory.SubmitAsync(company, requester, new { amount = 1 });

        var request = await factory.RequestAsync(id);
        request.Status.ShouldBe(ApprovalStatus.NeedsAttention);
        request.History.ShouldContain(h => h.Kind == ApprovalEventKind.NeedsAttention && h.Detail == ApprovalEngine.Reasons.NoApprover);
        (await factory.OutboxAsync<NotificationRequested>()).ShouldContain(n =>
            n.Type == PlatformNotificationTypes.ApprovalNeedsAttention && n.RecipientUserIds.Contains(factory.RootId) && n.Link == ApprovalEngine.Link(request.Id));

        (await factory.SendAsync(new AssignApproversCommand([approver]) { RequestId = request.Id.Value }, requester)).Error.Type.ShouldBe(ErrorType.Forbidden);
        (await factory.SendAsync(new AssignApproversCommand([approver]) { RequestId = request.Id.Value }, factory.RootId)).IsSuccess.ShouldBeTrue();
        await factory.DecideAsync(await factory.TaskOfAsync(id, approver), approver, ApprovalDecision.Approve);

        (await factory.RequestAsync(id)).Status.ShouldBe(ApprovalStatus.Approved);
    }

    [Fact]
    public async Task Without_a_definition_the_document_type_decides()
    {
        var company = await factory.CompanyAsync();
        var requester = await factory.UserAsync();

        var note = await factory.RequestAsync(await factory.SubmitAsync(company, requester, new { amount = 1 }, Note));
        var undefined = await factory.RequestAsync(await factory.SubmitAsync(company, requester, new { amount = 1 }, Undefined));

        note.Status.ShouldBe(ApprovalStatus.Approved);
        (await factory.OutboxAsync<ApprovalRequestCompleted>()).ShouldContain(e => e.DocumentId == note.DocumentId && e.Reason == ApprovalEngine.Reasons.NoDefinition);

        // Nothing to assign without steps: an administrator approves it
        undefined.Status.ShouldBe(ApprovalStatus.NeedsAttention);
        undefined.CurrentStep.ShouldBeNull();
        undefined.History.ShouldContain(h => h.Kind == ApprovalEventKind.NeedsAttention && h.Detail == ApprovalEngine.Reasons.NoDefinition);
        (await factory.SendAsync(new AssignApproversCommand([requester]) { RequestId = undefined.Id.Value }, factory.RootId)).Error.ShouldBe(ApprovalErrors.NoStep);
        (await factory.SendAsync(new ForceApproveApprovalCommand("Tanım yok") { RequestId = undefined.Id.Value }, factory.RootId)).IsSuccess.ShouldBeTrue();
        (await factory.RequestAsync(undefined.DocumentId)).Status.ShouldBe(ApprovalStatus.Approved);
    }

    [Fact]
    public async Task Scenario10_two_decisions_on_one_task_at_once_conflict()
    {
        var company = await factory.CompanyAsync();
        var (approver, requester) = (await factory.UserAsync(), await factory.UserAsync());
        await factory.DefineAsync(company, [Step(User(approver)), Step(User(await factory.UserAsync()))]);
        var task = await factory.TaskOfAsync(await factory.SubmitAsync(company, requester, new { amount = 1 }), approver);

        var results = await Task.WhenAll(
            factory.DecideAsync(task, approver, ApprovalDecision.Approve),
            factory.DecideAsync(task, approver, ApprovalDecision.Approve));

        results.Count(r => r.IsSuccess).ShouldBe(1);
        results.Single(r => r.IsFailure).Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public async Task Administrator_reassigns_and_force_approves()
    {
        var company = await factory.CompanyAsync();
        var (approver, other, requester) = (await factory.UserAsync(), await factory.UserAsync(), await factory.UserAsync());
        await factory.DefineAsync(company, [Step(User(approver)), Step(User(approver))]);
        var id = await factory.SubmitAsync(company, requester, new { amount = 1 });
        var task = await factory.TaskOfAsync(id, approver);

        (await factory.SendAsync(new ReassignApprovalTaskCommand(other) { TaskId = task }, approver)).Error.Type.ShouldBe(ErrorType.Forbidden);
        (await factory.SendAsync(new ReassignApprovalTaskCommand(other) { TaskId = task }, factory.RootId)).IsSuccess.ShouldBeTrue();
        (await factory.DecideAsync(task, approver, ApprovalDecision.Approve)).Error.ShouldBe(ApprovalRequest.TaskNotOpen);
        var request = await factory.RequestAsync(id);
        request.OpenTasks.Single().AssigneeUserId.ShouldBe(other);
        request.History.ShouldContain(h => h.Kind == ApprovalEventKind.Reassigned && h.UserId == factory.RootId);

        (await factory.SendAsync(new ForceApproveApprovalCommand("Acil") { RequestId = request.Id.Value }, factory.RootId)).IsSuccess.ShouldBeTrue();

        // Step 2 has the same approver, who did not approve step 1 themself: they are asked
        request = await factory.RequestAsync(id);
        request.CurrentStep.ShouldBe(1);
        request.OpenTasks.Single().AssigneeUserId.ShouldBe(approver);
        request.History.ShouldContain(h => h.Kind == ApprovalEventKind.ForceApproved && h.UserId == factory.RootId && h.Detail == "Acil");
    }

    [Fact]
    public async Task Cancelled_document_closes_its_tasks()
    {
        var company = await factory.CompanyAsync();
        var (approver, requester) = (await factory.UserAsync(), await factory.UserAsync());
        await factory.DefineAsync(company, [Step(User(approver))]);
        var id = await factory.SubmitAsync(company, requester, new { amount = 1 });

        (await factory.SendAsync(new CancelApprovalCommand(Expense, id))).IsSuccess.ShouldBeTrue();
        (await factory.SendAsync(new CancelApprovalCommand(Expense, id))).IsSuccess.ShouldBeTrue();
        (await factory.SendAsync(new CancelApprovalCommand(Expense, "missing"))).Error.ShouldBe(CommonErrors.NotFound);

        var request = await factory.RequestAsync(id);
        request.Status.ShouldBe(ApprovalStatus.Cancelled);
        request.Tasks.Single().Status.ShouldBe(ApprovalTaskStatus.Closed);
    }

    [Fact]
    public async Task Repeated_request_for_a_running_document_changes_nothing()
    {
        var company = await factory.CompanyAsync();
        var (approver, requester) = (await factory.UserAsync(), await factory.UserAsync());
        await factory.DefineAsync(company, [Step(User(approver))]);
        var id = await factory.SubmitAsync(company, requester, new { amount = 1 });

        await factory.SubmitAsync(company, requester, new { amount = 1 }, documentId: id);

        var request = await factory.RequestAsync(id);
        request.Round.ShouldBe(1);
        request.Tasks.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Definition_is_checked_against_the_facts_and_resolvers()
    {
        var company = await factory.CompanyAsync();
        var result = await factory.SendAsync(
            new CreateApprovalDefinitionCommand(
                Expense,
                company,
                Name("Hatalı"),
                0,
                Json("""{ "fact": "days", "op": "gt", "value": 5 }"""),
                [
                    Step(new ApproverRuleInput("nobody", null)),
                    Step(new ApproverRuleInput("user", null), condition: """{ "fact": "category", "op": "gt", "value": "A" }"""),
                ],
                null),
            factory.RootId);

        result.Error.Fields.Select(f => (f.Field, f.Code)).ShouldBe(
            [
                ("entryCondition", ApprovalConditions.UnknownFactCode),
                ("steps[0].approver.resolver", ApprovalDefinitionErrors.UnknownResolverCode),
                ("steps[1].approver.parameters.userId", ApprovalDefinitionErrors.MissingParameterCode),
                ("steps[1].condition", ApprovalConditions.InvalidOperatorCode),
            ],
            ignoreOrder: true);

        var unknown = await factory.SendAsync(
            new CreateApprovalDefinitionCommand("test.unknown", null, Name("X"), 0, null, [Step(User(Guid.NewGuid()))], null),
            factory.RootId);
        unknown.Error.Fields.ShouldContain(f => f.Field == "documentType" && f.Code == ApprovalErrors.UnknownDocumentTypeCode);
    }

    [Fact]
    public async Task Escalation_needs_a_due_time()
    {
        var result = await factory.SendAsync(
            new CreateApprovalDefinitionCommand(Expense, null, Name("X"), 0, null, [Step(User(Guid.NewGuid()), escalation: ApprovalEscalation.Notify)], null),
            factory.RootId);

        result.Error.Fields.ShouldContain(f => f.Field == "steps[0].dueHours" && f.Code == ApprovalDefinitionErrors.DueHoursRequiredCode);
    }

    [Fact]
    public async Task Managing_definitions_needs_the_permission()
    {
        var user = await factory.UserAsync();

        var result = await factory.SendAsync(
            new CreateApprovalDefinitionCommand(Expense, null, Name("X"), 0, null, [Step(User(user))], null),
            user);

        result.Error.Type.ShouldBe(ErrorType.Forbidden);
    }
}
