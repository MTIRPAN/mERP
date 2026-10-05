using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Application;
using Merp.Testing.Containers;
using Merp.Testing.Web;
using Merp.Web.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.BuildingBlocks.IntegrationTests.Web;

public sealed class WebHostFactory(PostgresFixture postgres) : MerpWebApplicationFactory(postgres)
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await CreateTenantAsync("acme");
    }

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["RateLimiting:Public:PermitLimit"] = "2",
        ["Jobs:DashboardTenants:0"] = "acme",
    };

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModule>(new WebProbeModule());
}

public sealed class WebHostTests(WebHostFactory factory) : IClassFixture<WebHostFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Authorized(params string[] permissions) => factory.CreateClientFor("user-1", "acme", permissions);

    [Fact]
    public async Task HealthEndpoints_AreAnonymous_AndReadyChecksPostgres()
    {
        var client = factory.CreateClient();

        (await client.GetAsync("/health/live", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var ready = await client.GetAsync("/health/ready", Ct);
        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ready.Content.ReadAsStringAsync(Ct)).ShouldContain("\"postgres\"");
    }

    [Fact]
    public async Task AnonymousRequest_GetsTurkishUnauthorizedProblem()
    {
        var problem = await ProblemAsync(await factory.CreateClient().GetAsync("/api/platform/system/info", Ct), HttpStatusCode.Unauthorized);

        problem.GetProperty("title").GetString().ShouldBe("Oturum açmanız gerekiyor");
        problem.GetProperty("detail").GetString().ShouldBe("Bu işlem için oturum açmanız gerekiyor.");
        problem.GetProperty("code").GetString().ShouldBe("common.errors.unauthorized");
        problem.TryGetProperty("traceId", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task MissingPermission_GetsTurkishForbiddenProblem()
    {
        var problem = await ProblemAsync(await Authorized().GetAsync("/api/platform/system/info", Ct), HttpStatusCode.Forbidden);

        problem.GetProperty("title").GetString().ShouldBe("Erişim reddedildi");
        problem.GetProperty("code").GetString().ShouldBe("common.errors.forbidden");
    }

    [Fact]
    public async Task ProtectedEndpoint_WithPermission_Succeeds()
    {
        var info = await Authorized(PlatformPermissions.SystemInfo.Read).GetFromJsonAsync<SystemInfoResponse>("/api/platform/system/info", Ct);

        info!.TenantCode.ShouldBe("acme");
        info.Subject.ShouldBe("user-1");
        info.Environment.ShouldBe("Testing");
        info.Culture.ShouldBe("tr-TR");
    }

    [Fact]
    public async Task ValidationError_ListsLocalizedFieldErrors()
    {
        var problem = await ProblemAsync(await Authorized(WebProbeModule.Permission).GetAsync("/api/webprobe/validation", Ct), HttpStatusCode.BadRequest);

        problem.GetProperty("title").GetString().ShouldBe("İstek geçersiz");
        problem.GetProperty("detail").GetString().ShouldBe("Bir veya daha fazla alan geçersiz.");
        problem.GetProperty("code").GetString().ShouldBe("common.errors.validation");
        problem.GetProperty("errors").GetProperty("name")[0].GetString().ShouldBe("Bu alan zorunludur.");
        problem.GetProperty("errors").GetProperty("title")[0].GetString().ShouldBe("En fazla 10 karakter girilebilir.");
        var fieldErrors = problem.GetProperty("fieldErrors");
        fieldErrors.GetArrayLength().ShouldBe(2);
        fieldErrors[1].GetProperty("code").GetString().ShouldBe("common.errors.max_length");
    }

    [Fact]
    public async Task ConflictResult_Maps409()
    {
        var problem = await ProblemAsync(await Authorized(WebProbeModule.Permission).GetAsync("/api/webprobe/conflict", Ct), HttpStatusCode.Conflict);

        problem.GetProperty("code").GetString().ShouldBe("common.errors.concurrency_conflict");
        problem.GetProperty("title").GetString().ShouldBe("Çakışma");
    }

    [Fact]
    public async Task UnhandledException_Returns500_WithoutInternals()
    {
        var response = await Authorized(WebProbeModule.Permission).GetAsync("/api/webprobe/throw", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldNotContain("secret internal detail");
        body.ShouldNotContain("InvalidOperationException");
        var problem = JsonDocument.Parse(body).RootElement;
        problem.GetProperty("title").GetString().ShouldBe("Sunucu hatası");
        problem.GetProperty("detail").GetString().ShouldBe("Beklenmeyen bir hata oluştu.");
    }

    [Fact]
    public async Task UnknownRoute_GetsTurkishNotFoundProblem()
    {
        var problem = await ProblemAsync(await Authorized().GetAsync("/api/does-not-exist", Ct), HttpStatusCode.NotFound);

        problem.GetProperty("title").GetString().ShouldBe("Bulunamadı");
    }

    [Fact]
    public async Task CompanyHeader_IsBound_AndValidated()
    {
        var companyId = Guid.CreateVersion7();
        var client = Authorized(WebProbeModule.Permission);
        client.DefaultRequestHeaders.Add("X-Company-Id", companyId.ToString());
        (await client.GetFromJsonAsync<Guid?>("/api/webprobe/company", Ct)).ShouldBe(companyId);

        var invalid = Authorized(WebProbeModule.Permission);
        invalid.DefaultRequestHeaders.Add("X-Company-Id", "not-a-guid");
        var problem = await ProblemAsync(await invalid.GetAsync("/api/webprobe/company", Ct), HttpStatusCode.BadRequest);
        problem.GetProperty("code").GetString().ShouldBe("common.errors.invalid_company_header");
    }

    [Fact]
    public async Task UnsupportedCulture_FallsBackToTurkish()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US");

        var response = await client.GetAsync("/api/platform/system/info", Ct);

        response.Content.Headers.ContentLanguage.ShouldContain("tr-TR");
        (await ProblemAsync(response, HttpStatusCode.Unauthorized)).GetProperty("title").GetString().ShouldBe("Oturum açmanız gerekiyor");
    }

    [Fact]
    public async Task PublicEndpoints_AreRateLimitedPerClient()
    {
        var client = factory.CreateClient();
        (await client.GetAsync("/api/public/acme/webprobe", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/api/public/acme/webprobe", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var limited = await client.GetAsync("/api/public/acme/webprobe", Ct);

        var problem = await ProblemAsync(limited, HttpStatusCode.TooManyRequests);
        problem.GetProperty("title").GetString().ShouldBe("Çok fazla istek");
        problem.GetProperty("code").GetString().ShouldBe("common.errors.too_many_requests");
    }

    [Fact]
    public async Task OpenApiDocument_DescribesKeycloakSecurity()
    {
        var document = JsonDocument.Parse(await factory.CreateClient().GetStringAsync("/openapi/v1.json", Ct)).RootElement;

        var scheme = document.GetProperty("components").GetProperty("securitySchemes").GetProperty("keycloak");
        scheme.GetProperty("type").GetString().ShouldBe("oauth2");
        scheme.GetProperty("flows").GetProperty("authorizationCode").GetProperty("authorizationUrl").GetString()!.ShouldEndWith("/protocol/openid-connect/auth");
        var operation = document.GetProperty("paths").GetProperty("/api/platform/system/info").GetProperty("get");
        operation.GetProperty("security")[0].TryGetProperty("keycloak", out _).ShouldBeTrue();
        operation.GetProperty("description").GetString()!.ShouldContain(PlatformPermissions.SystemInfo.Read);
    }

    [Fact]
    public async Task ScalarReference_IsServed() =>
        (await factory.CreateClient().GetAsync("/scalar/", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task JobsDashboard_RequiresSessionCookieIssuedWithPermission()
    {
        (await factory.CreateClient().GetAsync("/admin/jobs", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Authorized().PostAsync("/api/platform/jobs/dashboard-session", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var session = await Authorized(PlatformPermissions.Jobs.View).PostAsync("/api/platform/jobs/dashboard-session", null, Ct);
        session.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var cookie = session.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("merp.jobs=", StringComparison.Ordinal));
        cookie.ShouldContain("httponly", Case.Insensitive);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/jobs");
        request.Headers.Add("Cookie", cookie.Split(';')[0]);
        (await factory.CreateClient().SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SignalRHub_RequiresAuthentication() =>
        (await factory.CreateClient().PostAsync("/hubs/notifications/negotiate?negotiateVersion=1", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        response.StatusCode.ShouldBe(expected);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
    }
}

public sealed class UnhealthyDependencyFactory(PostgresFixture postgres) : MerpWebApplicationFactory(postgres)
{
    // Nothing listens on port 1: the Valkey check must fail.
    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["ConnectionStrings:Valkey"] = "localhost:1,connectTimeout=500,abortConnect=false",
    };
}

public sealed class ReadinessTests(UnhealthyDependencyFactory factory) : IClassFixture<UnhealthyDependencyFactory>
{
    [Fact]
    public async Task Ready_IsUnhealthy_WhenADependencyIsDown()
    {
        var response = await factory.CreateClient().GetAsync("/health/ready", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var report = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;
        var checks = report.GetProperty("checks").EnumerateArray().ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("status").GetString());
        checks["valkey"].ShouldBe("Unhealthy");
        checks["postgres"].ShouldBe("Healthy");
        (await factory.CreateClient().GetAsync("/health/live", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
