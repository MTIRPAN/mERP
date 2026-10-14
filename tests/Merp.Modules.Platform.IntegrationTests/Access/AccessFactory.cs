using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Platform.Api.Endpoints;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Application.Roles;
using Merp.Modules.Platform.Application.Users;
using Merp.SharedKernel.Results;
using Merp.Testing.Containers;
using Merp.Testing.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Merp.Modules.Platform.IntegrationTests.Access;

/// <summary>
/// The real permission checks (role assignments, PLAN P2) with two tenants. <c>root</c> becomes System
/// Administrator on first sign-in (bootstrap); Keycloak is replaced by <see cref="FakeIdentityAdmin"/>.
/// </summary>
public class AccessFactory(PostgresFixture postgres) : MerpWebApplicationFactory(postgres)
{
    public const string Root = "root";

    public FakeIdentityAdmin Identity { get; } = new();

    protected override bool UseTestAccessControl => false;

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Platform:Bootstrap:Users:root:0"] = "system-admin",
    };

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await CreateTenantAsync("acme");
        await CreateTenantAsync("globex");
    }

    protected override void ConfigureServices(IServiceCollection services) =>
        services.Replace(ServiceDescriptor.Singleton<IIdentityAdmin>(Identity));

    /// <summary>A signed-in identity; holds only what its role assignments give.</summary>
    public HttpClient As(string subject, string tenant = "acme") => CreateClientFor(subject, tenant);

    public HttpClient AsRoot(string tenant = "acme") => As(Root, tenant);

    public static string NewSubject(string prefix = "u") => prefix + Guid.NewGuid().ToString("N")[..10];

    /// <summary>Signs the identity in once (just-in-time provisioning) and returns its platform user id.</summary>
    public async Task<Guid> UserIdAsync(string subject, string tenant = "acme")
    {
        var me = await As(subject, tenant).GetFromJsonAsync<MeResponse>("/api/platform/me", TestContext.Current.CancellationToken);
        return me!.User.Id!.Value;
    }

    public async Task<Guid> CreateRoleAsync(string[] permissions, string tenant = "acme", string? code = null)
    {
        var response = await AsRoot(tenant).PostAsJsonAsync(
            "/api/platform/roles",
            new { code = code ?? "R" + Guid.NewGuid().ToString("N")[..10], name = new Dictionary<string, string> { ["tr-TR"] = "Test rolü" }, permissions },
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<EntityCreatedResponse>(TestContext.Current.CancellationToken))!.Id;
    }

    public async Task<Guid> AssignAsync(Guid userId, Guid roleId, string scopeType = "Tenant", Guid? scopeId = null, DateOnly? from = null, DateOnly? to = null, string tenant = "acme")
    {
        var response = await AsRoot(tenant).PostAsJsonAsync(
            $"/api/platform/users/{userId}/assignments",
            new { roleId, scopeType, scopeId, includeSubUnits = false, validFrom = from, validTo = to },
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<EntityCreatedResponse>(TestContext.Current.CancellationToken))!.Id;
    }

    /// <summary>A user (new identity) holding <paramref name="permissions"/> in the given scope.</summary>
    public async Task<(HttpClient Client, Guid UserId)> UserWithAsync(string[] permissions, string scopeType = "Tenant", Guid? scopeId = null)
    {
        var subject = NewSubject();
        var userId = await UserIdAsync(subject);
        await AssignAsync(userId, await CreateRoleAsync(permissions), scopeType, scopeId);
        return (As(subject), userId);
    }

    public async Task<RoleListItem> SystemRoleAsync(string code, string tenant = "acme") =>
        (await AsRoot(tenant).GetFromJsonAsync<PagedResult<RoleListItem>>("/api/platform/roles?pageSize=200", TestContext.Current.CancellationToken))!
            .Items.Single(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase));

    public async Task<UserDetails> UserAsync(Guid userId) =>
        (await AsRoot().GetFromJsonAsync<UserDetails>($"/api/platform/users/{userId}", TestContext.Current.CancellationToken))!;

    public static DateOnly Today => PlatformClock.Today(TimeProvider.System);
}

/// <summary>Keycloak admin API stand-in: remembers identities and e-mails; can simulate failures.</summary>
public sealed class FakeIdentityAdmin : IIdentityAdmin
{
    public ConcurrentDictionary<string, NewIdentity> Identities { get; } = new();

    public ConcurrentDictionary<string, bool> Enabled { get; } = new();

    public ConcurrentQueue<(string Subject, IdentityEmail Email)> Emails { get; } = new();

    /// <summary>Error the next call returns, e.g. <see cref="IdentityErrors.Unavailable"/>.</summary>
    public Error? NextError { get; set; }

    public Task<Result<string>> CreateUserAsync(NewIdentity identity, CancellationToken cancellationToken = default)
    {
        if (TakeError() is { } error)
        {
            return Task.FromResult(Result.Failure<string>(error));
        }

        var subject = Guid.NewGuid().ToString();
        Identities[subject] = identity;
        Enabled[subject] = true;
        return Task.FromResult(Result.Success(subject));
    }

    public Task<Result> SetEnabledAsync(string subject, bool enabled, CancellationToken cancellationToken = default)
    {
        if (TakeError() is { } error)
        {
            return Task.FromResult(Result.Failure(error));
        }

        Enabled[subject] = enabled;
        return Task.FromResult(Result.Success());
    }

    public Task<Result> SendActionsEmailAsync(string subject, IdentityEmail email, CancellationToken cancellationToken = default)
    {
        if (TakeError() is { } error)
        {
            return Task.FromResult(Result.Failure(error));
        }

        Emails.Enqueue((subject, email));
        return Task.FromResult(Result.Success());
    }

    private Error? TakeError()
    {
        var error = NextError;
        NextError = null;
        return error;
    }
}
