using Merp.Application.Abstractions.Messaging;
using Merp.Infrastructure.MultiTenancy;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Contracts.Calendars;
using Merp.Modules.Platform.Infrastructure.Persistence;
using Merp.Testing.Containers;
using Merp.Testing.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Platform.IntegrationTests.Calendars;

/// <summary>Tenant <c>acme</c> with the seeded Turkish holidays and schedules (PLAN P9).</summary>
public sealed class CalendarFactory(PostgresFixture postgres) : MerpWebApplicationFactory(postgres)
{
    /// <summary>Holds the System Administrator role through a real assignment, so the reminder finds them.</summary>
    public const string AdministratorSubject = "calendar-admin";

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        [$"Platform:Bootstrap:Users:{AdministratorSubject}:0"] = "system-admin",
    };

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await CreateTenantAsync("acme");
    }

    /// <summary>A user with every calendar and organization permission (Tenant scope).</summary>
    public HttpClient Administrator() =>
        CreateClientFor(
            AdministratorSubject,
            "acme",
            PlatformPermissions.Calendars.Read,
            PlatformPermissions.Calendars.Manage,
            PlatformPermissions.Companies.Read,
            PlatformPermissions.Companies.Manage,
            PlatformPermissions.Branches.Read,
            PlatformPermissions.Branches.Manage);

    public HttpClient Reader() => CreateClientFor("calendar-reader", "acme", PlatformPermissions.Calendars.Read);

    /// <summary>Runs <paramref name="work"/> in a scope of acme, as a background job would.</summary>
    public async Task<T> InTenantAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        var tenant = (await Services.GetRequiredService<ITenantStore>().FindByCodeAsync("acme", TestContext.Current.CancellationToken))!;
        await using var scope = Services.GetRequiredService<TenantScopeFactory>().CreateScope(tenant);
        return await work(scope.ServiceProvider);
    }

    public Task<Guid?> ResolveAsync(Guid companyId, Guid? branchId) =>
        InTenantAsync(services => services.GetRequiredService<IWorkingCalendar>().ResolveScheduleIdAsync(companyId, branchId, TestContext.Current.CancellationToken));

    public Task<int> RemindAsync(int year) =>
        InTenantAsync(async services =>
            (await services.GetRequiredService<ISender>().Send(new Application.Calendars.RemindMissingHolidaysCommand(year), TestContext.Current.CancellationToken)).Value);

    public Task<T> QueryAsync<T>(Func<PlatformDbContext, Task<T>> query) =>
        InTenantAsync(services => query(services.GetRequiredService<PlatformDbContext>()));
}
