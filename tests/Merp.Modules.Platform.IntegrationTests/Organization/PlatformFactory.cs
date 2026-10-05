using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.Persistence.Outbox;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Branches;
using Merp.Modules.Platform.Application.Companies;
using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Merp.Testing.Containers;
using Merp.Testing.Data;
using Merp.Testing.Web;
using Merp.Web.Errors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Platform.IntegrationTests.Organization;

/// <summary>API host for company, branch and location tests. Tests that count rows get a tenant of their own.</summary>
public sealed class PlatformFactory(PostgresFixture postgres) : MerpWebApplicationFactory(postgres)
{
    public const string Tenant = "acme";

    public static readonly string[] ManagerPermissions =
    [
        PlatformPermissions.Companies.Read,
        PlatformPermissions.Companies.Manage,
        PlatformPermissions.Branches.Read,
        PlatformPermissions.Branches.Manage,
        PlatformPermissions.ReferenceData.Read,
    ];

    /// <summary>The API's JSON settings: enums as strings.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await CreateTenantAsync(Tenant);
    }

    /// <summary>A fresh tenant, for tests that look at whole lists.</summary>
    public async Task<string> NewTenantAsync()
    {
        var code = "t" + Guid.NewGuid().ToString("N")[..10];
        await CreateTenantAsync(code);
        return code;
    }

    /// <summary>Tenant wide manager of companies and branches.</summary>
    public HttpClient Manager(string tenant = Tenant) => CreateClientFor("manager-1", tenant, ManagerPermissions);

    public static async Task<Guid> CreateCompanyAsync(HttpClient client, CreateCompanyCommand command)
    {
        var response = await client.PostAsJsonAsync("/api/platform/companies", command, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<CreatedResponse>(Ct))!.Id;
    }

    public static async Task<Guid> CreateBranchAsync(HttpClient client, CreateBranchCommand command)
    {
        var response = await client.PostAsJsonAsync("/api/platform/branches", command, Json, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<CreatedResponse>(Ct))!.Id;
    }

    /// <summary>Outbox messages of the tenant with the given event type name, payloads parsed.</summary>
    public async Task<List<JsonElement>> OutboxAsync(string tenant, string eventName)
    {
        var record = (await Services.GetRequiredService<ITenantStore>().FindByCodeAsync(tenant, Ct))!;
        await using var scope = Services.GetRequiredService<TenantScopeFactory>().CreateScope(record);
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var messages = await db.OutboxMessages.AsNoTracking().OrderBy(m => m.OccurredAt).ToListAsync(Ct);
        return [.. messages
            .Where(m => m.Type.EndsWith("." + eventName, StringComparison.Ordinal))
            .Select(m => JsonSerializer.Deserialize<JsonElement>(m.Payload, OutboxIntegrationEventPublisher.SerializerOptions))];
    }

    public static string NewCode(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    /// <summary>A valid company in İstanbul / Kadıköy.</summary>
    public static CreateCompanyCommand Company(string? code = null, string? title = null, string? shortName = null) => new(
        code ?? NewCode("C"),
        title ?? "ACME Sanayi ve Ticaret A.Ş.",
        shortName ?? "ACME",
        "Kadıköy",
        TurkishData.Vkn(),
        "0123456789000015",
        null,
        "Caferağa Mah. Moda Cad. No: 1",
        null,
        Locations.Istanbul,
        Locations.Kadikoy,
        "34710",
        "0216 123 45 67",
        "info@acme.example.com");

    /// <summary>A valid store branch in Ankara / Çankaya.</summary>
    public static CreateBranchCommand Branch(Guid companyId, string? code = null, string? name = null) => new(
        companyId,
        code ?? NewCode("B"),
        name ?? "Çankaya Mağazası",
        BranchType.Store,
        "Kızılay Mah. Atatürk Bulvarı No: 10",
        null,
        Locations.Ankara,
        Locations.Cankaya,
        "06420",
        null);

    internal static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    internal static List<(string Field, string Code)> FieldCodes(JsonElement problem) =>
        [.. problem.GetProperty("fieldErrors").EnumerateArray().Select(e => (e.GetProperty("field").GetString()!, e.GetProperty("code").GetString()!))];
}

/// <summary>Seeded reference data ids (provinceCode * 1000 + n, Turkish alphabetical order).</summary>
public static class Locations
{
    public const int Ankara = 6;
    public const int Cankaya = 6007;
    public const int Istanbul = 34;
    public const int Kadikoy = 34023;
    public const int Izmir = 35;
    public const int Konak = 35021;
}
