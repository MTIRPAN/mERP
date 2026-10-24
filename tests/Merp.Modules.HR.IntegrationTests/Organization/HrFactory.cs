using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Merp.Infrastructure.Messaging.Integration;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.Persistence;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Domain.Organization;
using Merp.Modules.HR.Infrastructure.Persistence;
using Merp.Modules.HR.IntegrationTests.Employees;
using Merp.Modules.Platform.Application.Access;
using Merp.Testing.Containers;
using Merp.Testing.Data;
using Merp.Testing.Web;
using Merp.Web.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Merp.Modules.HR.IntegrationTests.Organization;

/// <summary>Tenant <c>acme</c> with helpers to create companies (Platform), units, jobs and positions over HTTP.</summary>
public class HrFactory(PostgresFixture postgres) : MerpWebApplicationFactory(postgres)
{
    public const string Tenant = "acme";

    public static readonly string[] AllPermissions =
    [
        HrPermissions.Org.Read,
        HrPermissions.Org.Manage,
        HrPermissions.Employees.Read,
        HrPermissions.Employees.Create,
        HrPermissions.Employees.Update,
        HrPermissions.Employees.Terminate,
        HrPermissions.Employees.Export,
        HrPermissions.Employees.User,
        HrPermissions.Employees.Sensitive.Read,
        HrPermissions.Employees.Sensitive.Update,
        HrPermissions.Employees.Documents.Read,
        HrPermissions.Employees.Documents.Manage,
        HrPermissions.Leave.Settings.Read,
        HrPermissions.Leave.Settings.Manage,
        HrPermissions.Leave.Balances.Read,
        HrPermissions.Leave.Balances.Manage,
        "platform.companies.read",
        "platform.companies.manage",
        "platform.branches.read",
        "platform.branches.manage",
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await CreateTenantAsync(Tenant);
    }

    /// <summary>Keycloak of the employee–user link (D39).</summary>
    public FakeIdentityAdmin Identity { get; } = new();

    protected override void ConfigureServices(IServiceCollection services) =>
        services.Replace(ServiceDescriptor.Singleton<IIdentityAdmin>(Identity));

    /// <summary>
    /// Moves integration events between the modules until none is left: the outboxes of HR and Platform are sent and
    /// their inboxes processed, as the background services do outside tests.
    /// </summary>
    public async Task DeliverMessagesAsync()
    {
        var tenant = (await Services.GetRequiredService<ITenantStore>().FindByCodeAsync(Tenant, Ct))!;
        var modules = Services.GetServices<ModuleDatabase>().ToList();
        var dispatcher = Services.GetRequiredService<OutboxDispatcher>();
        var inbox = Services.GetRequiredService<InboxProcessor>();
        for (var round = 0; round < 5; round++)
        {
            var moved = 0;
            foreach (var module in modules)
            {
                await using var scope = Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
                moved += await dispatcher.DispatchAsync(scope.ServiceProvider, module, Ct);
            }

            foreach (var module in modules)
            {
                moved += await inbox.ProcessAsync(tenant, module, Ct);
            }

            if (moved == 0)
            {
                return;
            }
        }
    }

    /// <summary>A user holding every organization permission with the Tenant scope.</summary>
    public HttpClient Admin() => CreateClientFor("admin", Tenant, AllPermissions);

    public static string NewCode(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    public static async Task<Guid> CreateCompanyAsync(HttpClient client, string? code = null)
    {
        var body = new
        {
            code = code ?? NewCode("C"),
            title = "Acme Holding Anonim Şirketi",
            shortName = "Acme",
            taxOffice = "Kadıköy",
            taxNumber = TurkishData.Vkn(),
            address = new { line1 = "Atatürk Cad. No: 1", provinceCode = 34, districtCode = 1421, postalCode = "34710" },
        };
        return await CreatedIdAsync(await client.PostAsJsonAsync("/api/platform/companies", body, Ct));
    }

    public static async Task<Guid> CreateBranchAsync(HttpClient client, Guid companyId)
    {
        var body = new
        {
            companyId,
            code = NewCode("B"),
            name = "Gebze Fabrika",
            type = "Factory",
            address = new { line1 = "OSB 3. Cad. No: 5", provinceCode = 41, districtCode = 1338 },
        };
        return await CreatedIdAsync(await client.PostAsJsonAsync("/api/platform/branches", body, Ct));
    }

    public static object UnitBody(Guid companyId, Guid? parentId = null, string? code = null, string name = "Satış Direktörlüğü", OrgUnitType type = OrgUnitType.Directorate) =>
        new { companyId, parentId, code = code ?? NewCode("U"), name, type = type.ToString() };

    public static async Task<Guid> CreateUnitAsync(HttpClient client, Guid companyId, Guid? parentId = null, string name = "Satış Direktörlüğü", OrgUnitType type = OrgUnitType.Directorate) =>
        await CreatedIdAsync(await client.PostAsJsonAsync("/api/hr/org-units", UnitBody(companyId, parentId, name: name, type: type), Ct));

    public static async Task<Guid> CreateJobAsync(HttpClient client, string name = "Satış Müdürü") =>
        await CreatedIdAsync(await client.PostAsJsonAsync("/api/hr/jobs", new { code = NewCode("J"), name, family = "Satış", level = "M2" }, Ct));

    public static object PositionBody(Guid unitId, Guid jobId, string? code = null, Guid? reportsTo = null, int? norm = null) =>
        new { orgUnitId = unitId, jobId, code = code ?? NewCode("P"), reportsToPositionId = reportsTo, norm };

    public static async Task<Guid> CreatePositionAsync(HttpClient client, Guid unitId, Guid jobId, Guid? reportsTo = null, int? norm = null) =>
        await CreatedIdAsync(await client.PostAsJsonAsync("/api/hr/positions", PositionBody(unitId, jobId, reportsTo: reportsTo, norm: norm), Ct));

    public static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<EntityCreatedResponse>(Ct))!.Id;
    }

    /// <summary>Runs <paramref name="query"/> against the HR database of the tenant.</summary>
    public async Task<T> QueryAsync<T>(Func<HrDbContext, Task<T>> query)
    {
        var tenant = (await Services.GetRequiredService<ITenantStore>().FindByCodeAsync(Tenant, Ct))!;
        await using var scope = Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        return await query(scope.ServiceProvider.GetRequiredService<HrDbContext>());
    }

    /// <summary>Runs <paramref name="work"/> with the services of the tenant, as the system.</summary>
    public async Task<T> InTenantAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        var tenant = (await Services.GetRequiredService<ITenantStore>().FindByCodeAsync(Tenant, Ct))!;
        await using var scope = Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        return await work(scope.ServiceProvider);
    }

    /// <summary>The reporting lines of a company as (manager, subordinate, depth).</summary>
    public Task<List<(Guid Manager, Guid Subordinate, int Depth)>> ReportingLinesAsync(Guid companyId) =>
        QueryAsync(async db => (await db.ReportingLines.AsNoTracking().Where(r => r.CompanyId == companyId).ToListAsync(Ct))
            .Select(r => (r.ManagerPositionId.Value, r.SubordinatePositionId.Value, r.Depth))
            .ToList());

    /// <summary>The integration events of type <typeparamref name="T"/> in the outbox, oldest first.</summary>
    public async Task<IReadOnlyList<T>> OutboxAsync<T>()
    {
        var payloads = await QueryAsync(db => db.OutboxMessages.AsNoTracking()
            .Where(m => m.Type == typeof(T).FullName)
            .OrderBy(m => m.OccurredAt)
            .Select(m => m.Payload)
            .ToListAsync(Ct));
        return [.. payloads.Select(p => JsonSerializer.Deserialize<T>(p, JsonSerializerOptions.Web)!)];
    }

    public Task<string> PathAsync(Guid unitId) =>
        QueryAsync(db => db.OrgUnits.AsNoTracking().Where(u => u.Id == OrgUnitId.From(unitId)).Select(u => u.Path).SingleAsync(Ct));

    public Task<List<string?>> AuditedFieldsAsync(string entityType, Guid id) =>
        QueryAsync(db => db.AuditEntries.AsNoTracking()
            .Where(e => e.EntityType == entityType && e.EntityId == id.ToString())
            .Select(e => e.Field)
            .ToListAsync(Ct));
}

/// <summary>The same host on the fake clock (<see cref="MerpWebApplicationFactory.Time"/>, 5 January 2026 09:00 UTC).</summary>
public sealed class HrClockFactory(PostgresFixture postgres) : HrFactory(postgres)
{
    protected override bool UseFakeTime => true;
}

/// <summary>ProblemDetails as the web client sees it.</summary>
public sealed record Problem(int Status, string Code, string Detail, Dictionary<string, string[]>? Errors)
{
    public static async Task<Problem> ReadAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = json.RootElement;
        return new Problem(
            root.GetProperty("status").GetInt32(),
            root.GetProperty("code").GetString()!,
            root.GetProperty("detail").GetString()!,
            root.TryGetProperty("errors", out var errors) ? errors.Deserialize<Dictionary<string, string[]>>() : null);
    }
}
