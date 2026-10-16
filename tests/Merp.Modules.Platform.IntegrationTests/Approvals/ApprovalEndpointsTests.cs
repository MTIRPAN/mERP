using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Approvals;
using Merp.Modules.Platform.Contracts.Approvals;
using Merp.Modules.Platform.Domain.Approvals;
using RoleEntity = Merp.Modules.Platform.Domain.Roles.Role;
using Merp.Modules.Platform.IntegrationTests.Organization;
using Merp.SharedKernel.ValueObjects;
using Merp.Web.Endpoints;
using Microsoft.EntityFrameworkCore;
using static Merp.Modules.Platform.IntegrationTests.Approvals.ApprovalFactory;

namespace Merp.Modules.Platform.IntegrationTests.Approvals;

/// <summary>The approval endpoints (PLAN P10b): flows, preview, inbox, decisions, timeline, administration, delegations.</summary>
public sealed class ApprovalEndpointsTests(ApprovalFactory factory) : IClassFixture<ApprovalFactory>
{
    private const string Url = "/api/platform/approvals";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Definitions_AreCreatedReadAndChanged_WithTheirVersion()
    {
        var company = await factory.CompanyAsync();
        var approver = await factory.UserAsync();
        var created = await factory.AsRoot().PostAsJsonAsync($"{Url}/definitions", Definition(company, approver, """{ "fact": "amount", "op": "gte", "value": 100 }"""), Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var id = (await created.Content.ReadFromJsonAsync<EntityCreatedResponse>(Ct))!.Id;

        var list = (await factory.AsRoot().GetFromJsonAsync<List<ApprovalDefinitionListItem>>($"{Url}/definitions?documentType={Expense}", Ct))!;
        list.ShouldContain(d => d.Id == id && d.CompanyId == company && d.StepCount == 1 && d.DocumentTypeName == "test.expense");
        var details = (await factory.AsRoot().GetFromJsonAsync<ApprovalDefinitionDetails>($"{Url}/definitions/{id}", Ct))!;
        details.Steps.Single().Approver.Parameters["userId"].ShouldBe(approver.ToString());
        details.EntryCondition!.Value.GetProperty("op").GetString().ShouldBe("gte");

        var update = new
        {
            version = details.Version,
            name = details.Name,
            priority = 5,
            entryCondition = (object?)null,
            steps = details.Steps,
            fallback = (object?)null,
            isActive = false,
        };
        (await factory.AsRoot().PutAsJsonAsync($"{Url}/definitions/{id}", update, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await factory.AsRoot().PutAsJsonAsync($"{Url}/definitions/{id}", update, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var changed = (await factory.AsRoot().GetFromJsonAsync<ApprovalDefinitionDetails>($"{Url}/definitions/{id}", Ct))!;
        (changed.Priority, changed.IsActive, changed.Revision, changed.EntryCondition).ShouldBe((5, false, 2, null));
        (await factory.AsRoot().GetAsync($"{Url}/definitions/{Guid.NewGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task InvalidDefinitions_Return400_WithFieldErrors()
    {
        var response = await factory.AsRoot().PostAsJsonAsync(
            $"{Url}/definitions",
            new
            {
                documentType = Expense,
                name = Name("Hatalı"),
                priority = 0,
                entryCondition = JsonSerializer.Deserialize<JsonElement>("""{ "fact": "salary", "op": "gt", "value": 1 }"""),
                steps = new[] { new { name = Name("Adım"), approver = new { resolver = "user", parameters = new Dictionary<string, string>() }, mode = "Any" } },
            },
            Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = (await Problem.ReadAsync(response)).Errors!;
        errors.Keys.ShouldBe(["entryCondition", "steps[0].approver.parameters.userId"], ignoreOrder: true);
        errors["entryCondition"].ShouldBe(["\"salary\" bu belge türünde koşullarda kullanılabilen bir olgu değil."]);
        (await Problem.ReadAsync(await factory.AsRoot().PostAsJsonAsync($"{Url}/definitions", new { documentType = Expense, name = Name("X"), steps = Array.Empty<object>() }, Ct)))
            .Errors!.ShouldContainKey("steps");
    }

    [Fact]
    public async Task Flows_NeedTheirPermissions_WithTheTenantScope()
    {
        var (user, _) = await factory.ClientAsync();
        (await user.GetAsync($"{Url}/definitions", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await user.GetAsync($"{Url}/resolvers", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await user.PostAsJsonAsync($"{Url}/definitions", Definition(null, Guid.NewGuid()), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await user.GetAsync($"{Url}/requests", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A company administrator of flows still cannot change them: flows are shared by all companies
        var (companyAdmin, companyAdminId) = await factory.ClientAsync();
        var role = await factory.RoleAsync(PlatformPermissions.ApprovalDefinitions.Read, PlatformPermissions.ApprovalDefinitions.Manage);
        await factory.AssignAsync(companyAdminId, role, "Company", await factory.CompanyAsync());
        var denied = await companyAdmin.GetAsync($"{Url}/definitions", Ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Problem.ReadAsync(denied)).Code.ShouldBe(ApprovalErrors.TenantScopeRequired.Code);
    }

    [Fact]
    public async Task DocumentTypes_AndResolvers_AreListedWithTheirTexts()
    {
        var (user, _) = await factory.ClientAsync();
        var types = (await user.GetFromJsonAsync<List<ApprovalDocumentTypeItem>>($"{Url}/document-types", Ct))!;
        types.ShouldContain(t => t.Key == Note && t.AutoApproveWhenNoDefinition);
        types.Single(t => t.Key == Expense).Facts.Select(f => f.Name).ShouldBe(["amount", "category", "approverUserId", "branchId"]);

        var resolvers = (await factory.AsRoot().GetFromJsonAsync<List<ApproverResolverItem>>($"{Url}/resolvers", Ct))!;
        resolvers.Single(r => r.Key == "role").ShouldSatisfyAllConditions(
            r => r.Name.ShouldBe("Rol"),
            r => r.Parameters.Single().Name.ShouldBe("roleId"));
        // HR registers line-manager, org-unit-manager and position (D37)
        resolvers.Select(r => r.Key).ShouldBe(["fact-user", "role", "user", "line-manager", "org-unit-manager", "position"], ignoreOrder: true);
        resolvers.Single(r => r.Key == "line-manager").Name.ShouldBe("Hiyerarşik yönetici");
    }

    [Fact]
    public async Task DefaultFlows_AreSeeded_WithTheTenantsRoleIds()
    {
        var code = Code.Create("hr-specialist", RoleEntity.CodeMaxLength).Value.Value;
        var hrSpecialist = await factory.QueryAsync(db => db.Roles.Where(r => r.Code == code).Select(r => r.Id).SingleAsync(Ct));

        var seeded = await factory.QueryAsync(db => db.ApprovalDefinitions.AsNoTracking().SingleAsync(d => d.DocumentType == Seeded, Ct));

        seeded.CompanyId.ShouldBeNull();
        seeded.Name.Values["tr-TR"].ShouldBe("test.seeded");
        seeded.Steps.Select(s => s.Approver.Resolver).ShouldBe(["fact-user", "role"]);
        seeded.Steps[1].Approver.Parameters.ShouldBe(new Dictionary<string, string> { ["roleId"] = hrSpecialist.Value.ToString() });
        seeded.Steps[1].Condition.ShouldNotBeNull();
        (await factory.QueryAsync(db => db.ApprovalDefinitions.CountAsync(d => d.DocumentType == Note, Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task Preview_ShowsWhoWouldApprove()
    {
        var company = await factory.CompanyAsync();
        var (requester, _) = await factory.ClientAsync();
        var (manager, other) = (await factory.UserAsync(), await factory.UserAsync());
        await factory.DefineAsync(company, [Step(FactUser("approverUserId")), Step(User(other), condition: """{ "fact": "amount", "op": "gt", "value": 5 }"""), Step(User(manager))]);

        var response = await requester.PostAsJsonAsync($"{Url}/preview", new { documentType = Expense, companyId = company, facts = new { amount = 3, approverUserId = manager } }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var preview = (await response.Content.ReadFromJsonAsync<ApprovalPreview>(Ct))!;
        preview.HasDefinition.ShouldBeTrue();
        preview.Steps.Select(s => (s.Index, s.SkipReason, s.Approvers.Select(a => a.UserId).SingleOrDefault())).ShouldBe(
            [(0, null, manager), (1, ApprovalEngine.Reasons.Condition, Guid.Empty), (2, ApprovalEngine.Reasons.AlreadyApproved, Guid.Empty)]);
        preview.Steps[0].Name.ShouldBe("Adım");

        (await requester.PostAsJsonAsync($"{Url}/preview", new { documentType = "test.unknown" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var note = (await (await requester.PostAsJsonAsync($"{Url}/preview", new { documentType = Note, companyId = company }, Ct)).Content.ReadFromJsonAsync<ApprovalPreview>(Ct))!;
        (note.HasDefinition, note.AutoApproved).ShouldBe((false, true));
    }

    [Fact]
    public async Task MyTasks_DecisionAndTimeline_WorkForApproverAndDelegate()
    {
        var company = await factory.CompanyAsync();
        var (approver, approverId) = await factory.ClientAsync();
        var (@delegate, delegateId) = await factory.ClientAsync();
        var (requester, requesterId) = await factory.ClientAsync();
        var (stranger, _) = await factory.ClientAsync();
        var today = DateOnly.FromDateTime(factory.Time.GetUtcNow().UtcDateTime);
        (await approver.PostAsJsonAsync($"{Url}/delegations", new { delegateUserId = delegateId, from = today, to = today.AddDays(5), documentTypes = new[] { Expense } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        await factory.DefineAsync(company, [Step(User(approverId), allowReturn: true), Step(User(approverId))]);
        var documentId = await factory.SubmitAsync(company, requesterId, new { amount = 1 });

        var mine = (await approver.GetFromJsonAsync<PagedResult<MyApprovalTaskItem>>($"{Url}/my-tasks", Ct))!.Items.ShouldHaveSingleItem();
        (mine.DocumentId, mine.Title, mine.Summary, mine.StepName, mine.StepCount, mine.AllowReturn, mine.OnBehalfOfUserId).ShouldBe((documentId, "test.title", "Masraf", "Adım", 2, true, null));
        var delegated = (await @delegate.GetFromJsonAsync<PagedResult<MyApprovalTaskItem>>($"{Url}/my-tasks", Ct))!.Items.ShouldHaveSingleItem();
        delegated.OnBehalfOfUserId.ShouldBe(approverId);
        (await @delegate.GetFromJsonAsync<ApprovalTaskCount>($"{Url}/my-tasks/count", Ct))!.Count.ShouldBe(1);
        (await stranger.GetFromJsonAsync<ApprovalTaskCount>($"{Url}/my-tasks/count", Ct))!.Count.ShouldBe(0);

        // Who may see the request: requester, approver, delegate; not a stranger
        var details = (await requester.GetFromJsonAsync<ApprovalRequestDetails>($"{Url}/requests/{mine.RequestId}", Ct))!;
        (details.Status, details.MyTaskId, details.CanAdminister, details.RequesterUserId).ShouldBe((ApprovalStatus.Pending, null, false, requesterId));
        (await @delegate.GetFromJsonAsync<ApprovalRequestDetails>($"{Url}/requests/{mine.RequestId}", Ct))!.MyTaskId.ShouldBe(mine.TaskId);
        (await stranger.GetAsync($"{Url}/requests/{mine.RequestId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.GetAsync($"{Url}/timeline?documentType={Expense}&documentId={documentId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Decisions: a comment is needed to reject, strangers do not find the task, the delegate decides for the approver
        var noComment = await approver.PostAsJsonAsync($"{Url}/tasks/{mine.TaskId}/decision", new { decision = "Reject" }, Ct);
        noComment.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Problem.ReadAsync(noComment)).Errors!["comment"].ShouldBe(["Ret ve iade için açıklama yazılmalı."]);
        (await stranger.PostAsJsonAsync($"{Url}/tasks/{mine.TaskId}/decision", new { decision = "Approve" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await @delegate.PostAsJsonAsync($"{Url}/tasks/{mine.TaskId}/decision", new { decision = "Approve", comment = "Uygun" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await approver.PostAsJsonAsync($"{Url}/tasks/{mine.TaskId}/decision", new { decision = "Approve" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var timeline = (await approver.GetFromJsonAsync<ApprovalRequestDetails>($"{Url}/timeline?documentType={Expense}&documentId={documentId}", Ct))!;
        var approved = timeline.History.Single(h => h.Kind == ApprovalEventKind.Approved);
        (approved.UserId, approved.OnBehalfOfUserId, approved.Detail).ShouldBe((delegateId, approverId, "Uygun"));
        approved.UserName.ShouldNotBeNullOrEmpty();
        approved.OnBehalfOfName.ShouldNotBeNullOrEmpty();
        timeline.CurrentStep.ShouldBe(1);
        timeline.Steps.Select(s => s.Name).ShouldBe(["Adım", "Adım"]);
    }

    [Fact]
    public async Task Scenario10_two_decisions_over_http_one_gets_409()
    {
        var company = await factory.CompanyAsync();
        var (approver, approverId) = await factory.ClientAsync();
        await factory.DefineAsync(company, [Step(User(approverId)), Step(User(await factory.UserAsync()))]);
        var task = await factory.TaskOfAsync(await factory.SubmitAsync(company, await factory.UserAsync(), new { amount = 1 }), approverId);

        var responses = await Task.WhenAll(
            approver.PostAsJsonAsync($"{Url}/tasks/{task}/decision", new { decision = "Approve" }, Ct),
            approver.PostAsJsonAsync($"{Url}/tasks/{task}/decision", new { decision = "Approve" }, Ct));

        responses.Select(r => r.StatusCode).ShouldBe([HttpStatusCode.NoContent, HttpStatusCode.Conflict], ignoreOrder: true);
    }

    [Fact]
    public async Task Scenario11_request_delivered_twice_starts_one_flow()
    {
        var company = await factory.CompanyAsync();
        var (approver, requester) = (await factory.UserAsync(), await factory.UserAsync());
        await factory.DefineAsync(company, [Step(User(approver))]);
        var documentId = Guid.NewGuid().ToString();

        await factory.DeliverTwiceAsync(new ApprovalRequested(
            Expense, documentId, requester, null, JsonSerializer.SerializeToElement(new { amount = 1 }), "test.title", new Dictionary<string, string>(), null, null)
        {
            CompanyId = company,
        });

        var request = await factory.RequestAsync(documentId);
        request.Tasks.Count.ShouldBe(1);
        request.History.Count(h => h.Kind == ApprovalEventKind.Submitted).ShouldBe(1);
        (await factory.QueryAsync(db => db.InboxMessages.CountAsync(m => m.Type.Contains(nameof(ApprovalRequested)) && m.ProcessedAt != null, Ct))).ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Administrators_ListReassignForceApproveAndAssign()
    {
        var company = await factory.CompanyAsync();
        var (approver, other, requester) = (await factory.UserAsync(), await factory.UserAsync(), await factory.UserAsync());
        var (user, _) = await factory.ClientAsync();
        await factory.DefineAsync(company, [Step(User(approver)), Step(FactUser("approverUserId"))]);
        var documentId = await factory.SubmitAsync(company, requester, new { amount = 1 });
        var request = await factory.RequestAsync(documentId);
        var task = request.OpenTasks.Single().Id.Value;

        (await user.PostAsJsonAsync($"{Url}/tasks/{task}/reassign", new { userId = other }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var unknown = await factory.AsRoot().PostAsJsonAsync($"{Url}/tasks/{task}/reassign", new { userId = Guid.NewGuid() }, Ct);
        (await Problem.ReadAsync(unknown)).Errors!.ShouldContainKey("userId");
        (await factory.AsRoot().PostAsJsonAsync($"{Url}/tasks/{task}/reassign", new { userId = other }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await factory.AsRoot().GetFromJsonAsync<ApprovalRequestDetails>($"{Url}/requests/{request.Id.Value}", Ct))!.CanAdminister.ShouldBeTrue();

        // Approving step 1 in place of the approver leads to step 2, which finds nobody
        (await user.PostAsJsonAsync($"{Url}/requests/{request.Id.Value}/force-approve", new { comment = "Acil" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await factory.AsRoot().PostAsJsonAsync($"{Url}/requests/{request.Id.Value}/force-approve", new { comment = "Acil" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var attention = (await factory.AsRoot().GetFromJsonAsync<PagedResult<ApprovalRequestListItem>>($"{Url}/requests?status=NeedsAttention&documentType={Expense}&pageSize=200", Ct))!;
        attention.Items.ShouldContain(r => r.DocumentId == documentId && r.CurrentStep == 1);

        (await factory.AsRoot().PostAsJsonAsync($"{Url}/requests/{request.Id.Value}/approvers", new { userIds = Array.Empty<Guid>() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await factory.AsRoot().PostAsJsonAsync($"{Url}/requests/{request.Id.Value}/approvers", new { userIds = new[] { approver } }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await factory.AsRoot().PostAsJsonAsync($"{Url}/requests/{request.Id.Value}/approvers", new { userIds = new[] { approver } }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await factory.AsRoot().PostAsJsonAsync($"{Url}/requests/{Guid.NewGuid()}/force-approve", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await factory.RequestAsync(documentId)).OpenTasks.Single().AssigneeUserId.ShouldBe(approver);
    }

    [Fact]
    public async Task Delegations_AreTheCallersOwn()
    {
        var (principal, principalId) = await factory.ClientAsync();
        var (@delegate, delegateId) = await factory.ClientAsync();
        var today = DateOnly.FromDateTime(factory.Time.GetUtcNow().UtcDateTime);

        var self = await principal.PostAsJsonAsync($"{Url}/delegations", new { delegateUserId = principalId, from = today, to = today }, Ct);
        (await Problem.ReadAsync(self)).Errors!.ShouldContainKey("delegateUserId");
        var backwards = await principal.PostAsJsonAsync($"{Url}/delegations", new { delegateUserId = delegateId, from = today, to = today.AddDays(-1) }, Ct);
        (await Problem.ReadAsync(backwards)).Errors!.ShouldContainKey("to");
        var unknownType = await principal.PostAsJsonAsync($"{Url}/delegations", new { delegateUserId = delegateId, from = today, to = today, documentTypes = new[] { "x.y" } }, Ct);
        (await Problem.ReadAsync(unknownType)).Errors!.ShouldContainKey("documentTypes");

        var created = await principal.PostAsJsonAsync($"{Url}/delegations", new { delegateUserId = delegateId, from = today, to = today.AddDays(2), documentTypes = new[] { Expense } }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<EntityCreatedResponse>(Ct))!.Id;

        var given = (await principal.GetFromJsonAsync<List<DelegationItem>>($"{Url}/delegations", Ct))!.ShouldHaveSingleItem();
        (given.Given, given.DelegateUserId, given.DocumentTypeNames.Single()).ShouldBe((true, delegateId, "test.expense"));
        (await @delegate.GetFromJsonAsync<List<DelegationItem>>($"{Url}/delegations", Ct))!.ShouldHaveSingleItem().Given.ShouldBeFalse();

        (await @delegate.PostAsync($"{Url}/delegations/{id}/revoke", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await principal.PostAsync($"{Url}/delegations/{id}/revoke", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await principal.GetFromJsonAsync<List<DelegationItem>>($"{Url}/delegations", Ct))!.Single().IsRevoked.ShouldBeTrue();
    }

    [Fact]
    public async Task Users_CanBeSearchedToPickADelegate()
    {
        var (user, _) = await factory.ClientAsync();
        var subject = "zeynep" + Guid.NewGuid().ToString("N")[..6];
        var zeynep = await factory.UserAsync(subject);

        var found = (await user.GetFromJsonAsync<List<ApprovalUserOption>>($"{Url}/users?search={subject}", Ct))!;

        found.ShouldHaveSingleItem().Id.ShouldBe(zeynep);
        (await user.GetFromJsonAsync<List<ApprovalUserOption>>($"{Url}/users?search=nobody-matches-this&ids={zeynep}", Ct))!
            .ShouldHaveSingleItem().Id.ShouldBe(zeynep);
    }

    [Fact]
    public async Task TestDocuments_GoThroughTheOutbox_LikeAModulesDocument()
    {
        var (user, _) = await factory.ClientAsync();
        var approver = await factory.UserAsync();

        (await user.PostAsJsonAsync($"{Url}/test-documents", new { summary = "Deneme" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Problem.ReadAsync(await factory.AsRoot().PostAsJsonAsync($"{Url}/test-documents", new { amount = 5 }, Ct))).Errors!.ShouldContainKey("summary");

        var sent = await factory.AsRoot().PostAsJsonAsync($"{Url}/test-documents", new { summary = "Deneme masrafı", amount = 1250.5m, approverUserId = approver }, Ct);

        sent.StatusCode.ShouldBe(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync(Ct));
        var documentId = (await sent.Content.ReadFromJsonAsync<TestApprovalDocumentResponse>(Ct))!.DocumentId;
        var requested = (await factory.OutboxAsync<ApprovalRequested>()).Single(e => e.DocumentId == documentId);
        (requested.DocumentType, requested.RequesterUserId, requested.Summary).ShouldBe((ApprovalTestDocumentTypes.Key, factory.RootId, "Deneme masrafı"));
        requested.Facts.GetProperty("amount").GetDecimal().ShouldBe(1250.5m);
        requested.Facts.GetProperty("approverUserId").GetGuid().ShouldBe(approver);
        (await user.GetFromJsonAsync<List<ApprovalDocumentTypeItem>>($"{Url}/document-types", Ct))!.ShouldContain(t => t.Key == ApprovalTestDocumentTypes.Key);
    }

    private static object Definition(Guid? companyId, Guid approver, string? entryCondition = null) => new
    {
        documentType = Expense,
        companyId,
        name = Name("Masraf onayı"),
        priority = 0,
        entryCondition = entryCondition is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(entryCondition),
        steps = new[] { new { name = Name("Yönetici"), approver = User(approver), mode = "Any", escalation = "None" } },
    };
}
