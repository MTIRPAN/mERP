using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Merp.Infrastructure.MultiTenancy;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Organization;
using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Merp.Testing.Containers;
using Merp.Testing.Data;
using Merp.Testing.Web;
using Merp.Web.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Platform.IntegrationTests.Organization;

/// <summary>Two tenants, <c>acme</c> and <c>globex</c>, with helpers to create companies and branches over HTTP.</summary>
public sealed class OrganizationFactory(PostgresFixture postgres) : MerpWebApplicationFactory(postgres)
{
    public static readonly string[] AllPermissions =
    [
        PlatformPermissions.Companies.Read,
        PlatformPermissions.Companies.Manage,
        PlatformPermissions.Branches.Read,
        PlatformPermissions.Branches.Manage,
    ];

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await CreateTenantAsync("acme");
        await CreateTenantAsync("globex");
    }

    /// <summary>A user of <paramref name="tenant"/> holding every company and branch permission (Tenant scope).</summary>
    public HttpClient Admin(string tenant = "acme") => CreateClientFor("admin", tenant, AllPermissions);

    public static string NewCode(string prefix = "C") => prefix + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    public static object CompanyBody(string? code = null, string? taxNumber = null, string? title = null) => new
    {
        code = code ?? NewCode(),
        title = title ?? "Acme Holding Anonim Şirketi",
        shortName = "Acme",
        taxOffice = "Kadıköy",
        taxNumber = taxNumber ?? TurkishData.Vkn(),
        address = new { line1 = "Atatürk Cad. No: 1", provinceCode = 34, districtCode = 1421, postalCode = "34710" },
        phone = "0216 123 45 67",
        email = "info@acme.com.tr",
    };

    public static object BranchBody(Guid companyId, string? code = null, BranchType type = BranchType.Factory) => new
    {
        companyId,
        code = code ?? NewCode("B"),
        name = "Gebze Fabrika",
        type = type.ToString(),
        address = new AddressInput("OSB 3. Cad. No: 5", ProvinceCode: 41, DistrictCode: 1338),
    };

    public static async Task<Guid> CreateCompanyAsync(HttpClient client, object? body = null)
    {
        var response = await client.PostAsJsonAsync("/api/platform/companies", body ?? CompanyBody(), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<EntityCreatedResponse>(TestContext.Current.CancellationToken))!.Id;
    }

    public static async Task<Guid> CreateBranchAsync(HttpClient client, Guid companyId, BranchType type = BranchType.Factory)
    {
        var response = await client.PostAsJsonAsync("/api/platform/branches", BranchBody(companyId, type: type), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<EntityCreatedResponse>(TestContext.Current.CancellationToken))!.Id;
    }

    /// <summary>Runs <paramref name="query"/> against the Platform database of <paramref name="tenantCode"/>.</summary>
    public async Task<T> QueryAsync<T>(string tenantCode, Func<PlatformDbContext, Task<T>> query)
    {
        var tenant = (await Services.GetRequiredService<ITenantStore>().FindByCodeAsync(tenantCode, TestContext.Current.CancellationToken))!;
        await using var scope = Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        return await query(scope.ServiceProvider.GetRequiredService<PlatformDbContext>());
    }

    public Task<List<string>> OutboxTypesAsync(string tenantCode) =>
        QueryAsync(tenantCode, db => db.OutboxMessages.AsNoTracking().OrderBy(m => m.OccurredAt).Select(m => m.Type).ToListAsync(TestContext.Current.CancellationToken));

    public Task<List<string?>> AuditedFieldsAsync(string tenantCode, string entityType, Guid id) =>
        QueryAsync(tenantCode, db => db.AuditEntries.AsNoTracking()
            .Where(e => e.EntityType == entityType && e.EntityId == id.ToString())
            .Select(e => e.Field)
            .ToListAsync(TestContext.Current.CancellationToken));
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
