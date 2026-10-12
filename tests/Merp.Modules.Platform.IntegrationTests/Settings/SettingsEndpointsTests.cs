using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Settings;
using Merp.Infrastructure.MultiTenancy;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Settings;
using Merp.Modules.Platform.IntegrationTests.Organization;
using Merp.Testing.Containers;
using Merp.Testing.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Platform.IntegrationTests.Settings;

/// <summary>Tenant <c>acme</c> with the settings of a test module next to Platform's (PLAN P11a, D35).</summary>
public sealed class SettingsFactory(PostgresFixture postgres) : MerpWebApplicationFactory(postgres)
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await CreateTenantAsync("acme");
    }

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<ISettingDefinitionProvider, TestSettings>();

    public HttpClient Administrator() =>
        CreateClientFor("settings-admin", "acme", PlatformPermissions.Settings.Read, PlatformPermissions.Settings.Manage, PlatformPermissions.Companies.Manage, PlatformPermissions.Companies.Read);

    /// <summary>Reads a setting as a module would, in a scope of acme.</summary>
    public async Task<T> ReadAsync<T>(string key, Guid? companyId = null)
    {
        var tenant = (await Services.GetRequiredService<ITenantStore>().FindByCodeAsync("acme", TestContext.Current.CancellationToken))!;
        await using var scope = Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        return await scope.ServiceProvider.GetRequiredService<ISettings>().GetAsync<T>(key, companyId, TestContext.Current.CancellationToken);
    }
}

public sealed class TestSettings : ISettingDefinitionProvider
{
    public const string Threshold = "test.leave.threshold";
    public const string Mode = "test.users.deactivation";
    public const string Footer = "test.mail.footer";

    public IEnumerable<SettingDefinition> GetSettings() =>
    [
        SettingDefinition.Number(Threshold, 3, "test.threshold", companyOverridable: true, min: 1, max: 50, wholeNumber: true),
        SettingDefinition.Choice(Mode, "immediately", [new("immediately", "test.immediately"), new("on_exit_date", "test.on_exit_date")], "test.mode"),
        SettingDefinition.Text(Footer, "", "test.footer", maxLength: 20),
    ];
}

public sealed class SettingsEndpointsTests(SettingsFactory factory) : IClassFixture<SettingsFactory>
{
    private const string Url = "/api/platform/settings";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Settings_AreGroupedByModule_WithTheirDefaults()
    {
        var groups = (await factory.Administrator().GetFromJsonAsync<List<SettingModuleGroup>>(Url, Ct))!;

        var platform = groups.Single(g => g.Module == "platform");
        platform.Name.ShouldBe("Platform");
        var email = platform.Settings.Single(s => s.Key == PlatformSettingDefinitions.EmailNotificationsEnabled);
        (email.Label, email.Type, email.CompanyOverridable).ShouldBe(("Bildirimleri e-postayla da gönder", SettingType.Boolean, true));
        email.InheritedValue.GetBoolean().ShouldBeTrue();
        var test = groups.Single(g => g.Module == "test").Settings;
        test.Select(s => s.Key).ShouldBe([TestSettings.Threshold, TestSettings.Mode, TestSettings.Footer], ignoreOrder: true);
        test.Single(s => s.Key == TestSettings.Mode).Options.Select(o => o.Value).ShouldBe(["immediately", "on_exit_date"]);
    }

    [Fact]
    public async Task CompanyValue_WinsOverTheTenants_WhichWinsOverTheDefault()
    {
        var admin = factory.Administrator();
        var (company, other) = (await OrganizationFactory.CreateCompanyAsync(admin), await OrganizationFactory.CreateCompanyAsync(admin));
        (await factory.ReadAsync<int>(TestSettings.Threshold, company)).ShouldBe(3);

        await SaveAsync(null, new() { [TestSettings.Threshold] = 5, [TestSettings.Mode] = "on_exit_date" });
        await SaveAsync(company, new() { [TestSettings.Threshold] = 8 });

        (await factory.ReadAsync<int>(TestSettings.Threshold, company)).ShouldBe(8);
        (await factory.ReadAsync<int>(TestSettings.Threshold, other)).ShouldBe(5);
        (await factory.ReadAsync<int>(TestSettings.Threshold)).ShouldBe(5);
        (await factory.ReadAsync<string>(TestSettings.Mode, company)).ShouldBe("on_exit_date");

        // The company sees only what it may override, with the tenant's value underneath
        var companyView = (await admin.GetFromJsonAsync<List<SettingModuleGroup>>($"{Url}?companyId={company}", Ct))!.SelectMany(g => g.Settings).ToList();
        companyView.ShouldNotContain(s => s.Key == TestSettings.Mode);
        var threshold = companyView.Single(s => s.Key == TestSettings.Threshold);
        (threshold.Value!.Value.GetInt32(), threshold.InheritedValue.GetInt32(), threshold.DefaultValue.GetInt32()).ShouldBe((8, 5, 3));

        // Null takes the company's own value away again
        await SaveAsync(company, new() { [TestSettings.Threshold] = null });
        (await factory.ReadAsync<int>(TestSettings.Threshold, company)).ShouldBe(5);
        await SaveAsync(null, new() { [TestSettings.Threshold] = null, [TestSettings.Mode] = null });
        (await factory.ReadAsync<int>(TestSettings.Threshold, company)).ShouldBe(3);
    }

    [Fact]
    public async Task InvalidValues_Return400_PerSetting()
    {
        var company = await OrganizationFactory.CreateCompanyAsync(factory.Administrator());
        var response = await factory.Administrator().PutAsJsonAsync(
            Url,
            new
            {
                values = new Dictionary<string, object?>
                {
                    [TestSettings.Threshold] = 99,
                    [TestSettings.Mode] = "never",
                    [TestSettings.Footer] = new string('x', 21),
                    [PlatformSettingDefinitions.EmailNotificationsEnabled] = "yes",
                    ["test.unknown"] = 1,
                },
            },
            Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = (await Problem.ReadAsync(response)).Errors!;
        errors[$"values.{TestSettings.Threshold}"].ShouldBe(["Değer 1 ile 50 arasında olmalıdır."]);
        errors.Keys.ShouldBe(
            [$"values.{TestSettings.Threshold}", $"values.{TestSettings.Mode}", $"values.{TestSettings.Footer}", $"values.{PlatformSettingDefinitions.EmailNotificationsEnabled}", "values.test.unknown"],
            ignoreOrder: true);

        var notForCompanies = await factory.Administrator().PutAsJsonAsync(Url, new { companyId = company, values = new Dictionary<string, object?> { [TestSettings.Mode] = "immediately" } }, Ct);
        (await Problem.ReadAsync(notForCompanies)).Errors!.ShouldContainKey($"values.{TestSettings.Mode}");
        var fraction = await factory.Administrator().PutAsJsonAsync(Url, new { values = new Dictionary<string, object?> { [TestSettings.Threshold] = 2.5 } }, Ct);
        fraction.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var unknownCompany = await factory.Administrator().PutAsJsonAsync(Url, new { companyId = Guid.NewGuid(), values = new Dictionary<string, object?>() }, Ct);
        (await Problem.ReadAsync(unknownCompany)).Errors!.ShouldContainKey("companyId");
        (await factory.Administrator().GetAsync($"{Url}?companyId={Guid.NewGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Settings_NeedThePermission_WithTheTenantScope()
    {
        (await factory.CreateClientFor("reader", "acme").GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var reader = factory.CreateClientFor("reader", "acme", PlatformPermissions.Settings.Read);
        (await reader.PutAsJsonAsync(Url, new { values = new Dictionary<string, object?>() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var companyAdmin = factory.Administrator().WithScopes(AccessScope.Company(Guid.NewGuid()));
        var denied = await companyAdmin.GetAsync(Url, Ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Problem.ReadAsync(denied)).Code.ShouldBe(SettingErrors.TenantScopeRequired.Code);
    }

    private async Task SaveAsync(Guid? companyId, Dictionary<string, object?> values)
    {
        var response = await factory.Administrator().PutAsJsonAsync(Url, new { companyId, values }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
    }
}
