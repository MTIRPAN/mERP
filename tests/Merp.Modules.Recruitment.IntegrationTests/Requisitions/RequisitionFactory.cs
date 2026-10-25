using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Merp.Infrastructure.Messaging.Integration;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.Persistence;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Recruitment.Infrastructure.Persistence;
using Merp.SharedKernel.Results;
using Merp.Testing.Containers;
using Merp.Testing.Web;
using Merp.Web.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Merp.Modules.Recruitment.IntegrationTests.Requisitions;

/// <summary>Tenant <c>acme</c> with the employee–user link and the delivery of integration events between the modules.</summary>
public sealed class RequisitionFactory(PostgresFixture postgres) : MerpWebApplicationFactory(postgres)
{
    public const string Tenant = "acme";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Keycloak of the employee–user link (D39).</summary>
    public FakeIdentityAdmin Identity { get; } = new();

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await CreateTenantAsync(Tenant);
    }

    protected override void ConfigureServices(IServiceCollection services) =>
        services.Replace(ServiceDescriptor.Singleton<IIdentityAdmin>(Identity));

    /// <summary>Moves integration events between the modules until none is left, as the background services do outside tests.</summary>
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

    /// <summary>The integration events of type <typeparamref name="T"/> in the outbox of the Recruitment module, oldest first.</summary>
    public async Task<IReadOnlyList<T>> OutboxAsync<T>()
    {
        var tenant = (await Services.GetRequiredService<ITenantStore>().FindByCodeAsync(Tenant, Ct))!;
        await using var scope = Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        var payloads = await scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>().OutboxMessages.AsNoTracking()
            .Where(m => m.Type == typeof(T).FullName)
            .OrderBy(m => m.OccurredAt)
            .Select(m => m.Payload)
            .ToListAsync(Ct);
        return [.. payloads.Select(p => JsonSerializer.Deserialize<T>(p, JsonSerializerOptions.Web)!)];
    }

    public static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<EntityCreatedResponse>(Ct))!.Id;
    }
}

/// <summary>Keycloak for the employee–user link: identities are kept in memory.</summary>
public sealed class FakeIdentityAdmin : IIdentityAdmin
{
    public ConcurrentDictionary<string, NewIdentity> Identities { get; } = new();

    public Task<Result<string>> CreateUserAsync(NewIdentity identity, CancellationToken cancellationToken = default)
    {
        var subject = Guid.NewGuid().ToString();
        Identities[subject] = identity;
        return Task.FromResult(Result.Success(subject));
    }

    public Task<Result> SetEnabledAsync(string subject, bool enabled, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

    public Task<Result> SendActionsEmailAsync(string subject, IdentityEmail email, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());
}
