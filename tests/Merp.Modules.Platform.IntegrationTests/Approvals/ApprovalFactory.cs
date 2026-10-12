using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Merp.Application.Abstractions.Approvals;
using Merp.Application.Abstractions.Messaging;
using Merp.Infrastructure.Messaging.Integration;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.Persistence;
using Merp.Infrastructure.Persistence.Outbox;
using Merp.Infrastructure.Security;
using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Application.Approvals;
using Merp.Modules.Platform.Contracts.Approvals;
using Merp.Modules.Platform.Domain.Approvals;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Merp.Modules.Platform.IntegrationTests.Organization;
using Merp.SharedKernel.Results;
using Merp.Testing.Containers;
using Merp.Testing.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Platform.IntegrationTests.Approvals;

/// <summary>
/// The approval engine (PLAN P10a) in tenant <c>acme</c> with the real role assignments and a fake clock. Commands run
/// in a tenant scope as a given user, as the inbox, the job and (from P10b) the endpoints run them. Every test uses its
/// own company: company definitions win over general ones, so tests do not see each other's flows.
/// </summary>
public sealed class ApprovalFactory(PostgresFixture postgres) : MerpWebApplicationFactory(postgres)
{
    public const string Root = "approval-root";
    public const string Tenant = "acme";

    /// <summary>Facts: amount, category, approverUserId, branchId.</summary>
    public const string Expense = "test.expense";

    /// <summary>Approved at once when no definition matches.</summary>
    public const string Note = "test.note";

    /// <summary>No definition is ever made: requests need attention.</summary>
    public const string Undefined = "test.undefined";

    /// <summary>Its default flow (fact-user, then the HR Specialist role above 5) is seeded in every tenant.</summary>
    public const string Seeded = "test.seeded";

    /// <summary>Only general definitions, for the definition selection test.</summary>
    public const string Purchase = "test.purchase";

    private Guid _rootId;

    public FakeManagerLookup Managers { get; } = new();

    protected override bool UseTestAccessControl => false;

    protected override bool UseFakeTime => true;

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        [$"Platform:Bootstrap:Users:{Root}:0"] = "system-admin",
        ["Platform:Approvals:EnableTestDocuments"] = "true",
    };

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await CreateTenantAsync(Tenant);
        _rootId = await UserAsync(Root);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IApprovalDocumentTypeProvider, TestDocumentTypes>();
        services.AddSingleton<IApproverManagerLookup>(Managers);
    }

    /// <summary>System Administrator (Tenant scope), holds <c>platform.approvals.admin</c>.</summary>
    public Guid RootId => _rootId;

    public HttpClient AsRoot() => CreateClientFor(Root, Tenant);

    /// <summary>A new signed-in user and their client; they hold no role.</summary>
    public async Task<(HttpClient Client, Guid UserId)> ClientAsync()
    {
        var subject = "u" + Guid.NewGuid().ToString("N")[..10];
        var userId = await UserAsync(subject);
        return (CreateClientFor(subject, Tenant), userId);
    }

    /// <summary>
    /// A module's event reaches the Platform inbox twice (the transport delivers at least once), then the inbox is
    /// processed twice (§2.9 scenario 11).
    /// </summary>
    public async Task DeliverTwiceAsync(ApprovalRequested requested)
    {
        var tenant = (await Services.GetRequiredService<ITenantStore>().FindByCodeAsync(Tenant, TestContext.Current.CancellationToken))!;
        var now = Time.GetUtcNow();
        var stamped = requested with { TenantId = tenant.Id, OccurredAt = now };
        var envelope = new MessageEnvelope(
            stamped.EventId,
            typeof(ApprovalRequested).FullName!,
            JsonSerializer.Serialize(stamped, OutboxIntegrationEventPublisher.SerializerOptions),
            tenant.Id,
            now,
            null);
        var transport = Services.GetRequiredService<IMessageTransport>();
        await using (var scope = Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant))
        {
            await transport.SendAsync(envelope, scope.ServiceProvider, TestContext.Current.CancellationToken);
            await transport.SendAsync(envelope, scope.ServiceProvider, TestContext.Current.CancellationToken);
        }

        var platform = Services.GetServices<ModuleDatabase>().Single(m => m.Schema == PlatformDbContext.SchemaName);
        var inbox = Services.GetRequiredService<InboxProcessor>();
        await inbox.ProcessAsync(tenant, platform, TestContext.Current.CancellationToken);
        await inbox.ProcessAsync(tenant, platform, TestContext.Current.CancellationToken);
    }

    /// <summary>Signs a new identity in (just-in-time provisioning) and returns its platform user id.</summary>
    public async Task<Guid> UserAsync(string? subject = null)
    {
        var me = await CreateClientFor(subject ?? "u" + Guid.NewGuid().ToString("N")[..10], Tenant)
            .GetFromJsonAsync<MeResponse>("/api/platform/me", TestContext.Current.CancellationToken);
        return me!.User.Id!.Value;
    }

    public Task<Guid> CompanyAsync() => OrganizationFactory.CreateCompanyAsync(AsRoot());

    public async Task<Guid> RoleAsync(params string[] permissions)
    {
        var response = await AsRoot().PostAsJsonAsync(
            "/api/platform/roles",
            new { code = "R" + Guid.NewGuid().ToString("N")[..10], name = new Dictionary<string, string> { ["tr-TR"] = "Onaycı" }, permissions = permissions.Length > 0 ? permissions : ["platform.companies.read"] },
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<EntityCreatedResponse>(TestContext.Current.CancellationToken))!.Id;
    }

    public async Task AssignAsync(Guid userId, Guid roleId, string scopeType = "Tenant", Guid? scopeId = null)
    {
        var response = await AsRoot().PostAsJsonAsync(
            $"/api/platform/users/{userId}/assignments",
            new { roleId, scopeType, scopeId, includeSubUnits = false },
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Runs a request in a scope of acme as <paramref name="user"/>, or as the system.</summary>
    public async Task<TResult> SendAsync<TResult>(IRequest<TResult> request, Guid? user = null)
        where TResult : Result
    {
        var tenant = (await Services.GetRequiredService<ITenantStore>().FindByCodeAsync(Tenant, TestContext.Current.CancellationToken))!;
        await using var scope = Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        if (user is { } userId)
        {
            scope.ServiceProvider.GetRequiredService<CurrentUser>().SetUser(userId, tenant.Id);
        }

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, TestContext.Current.CancellationToken);
    }

    public async Task<T> QueryAsync<T>(Func<PlatformDbContext, Task<T>> query)
    {
        var tenant = (await Services.GetRequiredService<ITenantStore>().FindByCodeAsync(Tenant, TestContext.Current.CancellationToken))!;
        await using var scope = Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        return await query(scope.ServiceProvider.GetRequiredService<PlatformDbContext>());
    }

    /// <summary>A company flow of <see cref="Expense"/> (or another type) as the System Administrator.</summary>
    public async Task<Guid> DefineAsync(Guid? companyId, IReadOnlyList<ApprovalStepInput> steps, string documentType = Expense, int priority = 0, string? entryCondition = null, ApproverRuleInput? fallback = null)
    {
        var result = await SendAsync(
            new CreateApprovalDefinitionCommand(documentType, companyId, Name("Akış"), priority, Json(entryCondition), steps, fallback),
            RootId);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? $"{result.Error.Code} {string.Join(", ", result.Error.Fields.Select(f => f.Field + ":" + f.Code))}" : null);
        return result.Value;
    }

    /// <summary>A module sends a document (as the inbox handler would); returns the document id.</summary>
    public async Task<string> SubmitAsync(Guid companyId, Guid requester, object facts, string documentType = Expense, string? documentId = null)
    {
        var id = documentId ?? Guid.NewGuid().ToString();
        var result = await SendAsync(new StartApprovalCommand(
            new ApprovalRequested(documentType, id, requester, null, JsonSerializer.SerializeToElement(facts), "test.title", new Dictionary<string, string>(), "Masraf", $"/test/{id}")
            {
                CompanyId = companyId,
            }));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        return id;
    }

    public Task<ApprovalRequest> RequestAsync(string documentId) =>
        QueryAsync(db => db.ApprovalRequests.AsNoTracking().Include(r => r.Tasks).Include(r => r.History)
            .SingleAsync(r => r.DocumentId == documentId, TestContext.Current.CancellationToken));

    /// <summary>The open task of <paramref name="user"/> on the document.</summary>
    public async Task<Guid> TaskOfAsync(string documentId, Guid user) =>
        (await RequestAsync(documentId)).OpenTasks.Single(t => t.AssigneeUserId == user).Id.Value;

    public Task<Result> DecideAsync(Guid taskId, Guid user, ApprovalDecision decision, string? comment = null) =>
        SendAsync(new DecideApprovalTaskCommand(decision, comment) { TaskId = taskId }, user);

    /// <summary>Events of a type written to the Platform outbox, e.g. ApprovalRequestCompleted.</summary>
    public async Task<IReadOnlyList<T>> OutboxAsync<T>()
    {
        var payloads = await QueryAsync(db => db.OutboxMessages.AsNoTracking()
            .Where(m => m.Type.Contains(typeof(T).Name))
            .OrderBy(m => m.OccurredAt)
            .Select(m => m.Payload)
            .ToListAsync(TestContext.Current.CancellationToken));
        return [.. payloads.Select(p => JsonSerializer.Deserialize<T>(p, JsonSerializerOptions.Web)!)];
    }

    public static IReadOnlyDictionary<string, string?> Name(string text) => new Dictionary<string, string?> { ["tr-TR"] = text };

    public static JsonElement? Json(string? json) => json is null ? null : JsonDocument.Parse(json).RootElement.Clone();

    public static ApproverRuleInput User(Guid userId) => new("user", new Dictionary<string, string?> { ["userId"] = userId.ToString() });

    public static ApproverRuleInput Role(Guid roleId) => new("role", new Dictionary<string, string?> { ["roleId"] = roleId.ToString() });

    public static ApproverRuleInput FactUser(string fact) => new("fact-user", new Dictionary<string, string?> { ["factName"] = fact });

    public static ApprovalStepInput Step(
        ApproverRuleInput approver,
        ApprovalMode mode = ApprovalMode.Any,
        string? condition = null,
        int? dueHours = null,
        ApprovalEscalation escalation = ApprovalEscalation.None,
        bool skipIfRequester = true,
        bool allowReturn = false) =>
        new(Name("Adım"), approver, mode, Json(condition), dueHours, escalation, skipIfRequester, SkipIfAlreadyApproved: true, allowReturn);
}

public sealed class TestDocumentTypes : IApprovalDocumentTypeProvider
{
    public IEnumerable<ApprovalDocumentType> GetDocumentTypes() =>
    [
        new(ApprovalFactory.Expense, "test.expense", Facts()),
        new(ApprovalFactory.Purchase, "test.purchase", Facts()),
        new(ApprovalFactory.Note, "test.note", Facts(), AutoApproveWhenNoDefinition: true),
        new(ApprovalFactory.Undefined, "test.undefined", Facts()),
        new(
            ApprovalFactory.Seeded,
            "test.seeded",
            Facts(),
            DefaultFlow:
            [
                new("test.step.manager", "fact-user", new Dictionary<string, string> { ["factName"] = "approverUserId" }),
                new(
                    "test.step.hr",
                    "role",
                    new Dictionary<string, string> { [ApprovalDefaultStep.RoleCodeParameter] = "hr-specialist" },
                    """{ "fact": "amount", "op": "gt", "value": 5 }"""),
            ]),
    ];

    private static ApprovalFact[] Facts() =>
    [
        new("amount", ApprovalFactType.Number, "test.amount"),
        new("category", ApprovalFactType.Text, "test.category"),
        new("approverUserId", ApprovalFactType.Identifier, "test.approver"),
        new("branchId", ApprovalFactType.Identifier, "test.branch"),
    ];
}

/// <summary>HR's manager lookup stand-in for <c>escalate-to-manager</c>: approver → manager.</summary>
public sealed class FakeManagerLookup : IApproverManagerLookup
{
    public ConcurrentDictionary<Guid, Guid> Managers { get; } = new();

    public Task<Guid?> FindManagerUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<Guid?>(Managers.TryGetValue(userId, out var manager) ? manager : null);
}
