using Hangfire;
using Hangfire.Client;
using Hangfire.Common;
using Hangfire.Storage;
using Merp.Application.Abstractions.Context;
using Merp.Infrastructure;
using Merp.Infrastructure.Jobs;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.MultiTenancy.Catalog;
using Merp.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Merp.BuildingBlocks.UnitTests.Jobs;

public sealed class TenantJobContextTests : IDisposable
{
    private static readonly TenantRecord Tenant = new(Guid.CreateVersion7(), "alpha", "Alpha", "tr-TR", TenantStatus.Active, "Host=alpha");
    private static readonly Guid UserId = Guid.CreateVersion7();

    private readonly ServiceProvider _provider;
    private readonly ITenantStore _tenantStore = Substitute.For<ITenantStore>();

    public TenantJobContextTests()
    {
        _tenantStore.FindByIdAsync(Tenant.Id, Arg.Any<CancellationToken>()).Returns(Tenant);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMerpInfrastructure();
        services.AddSingleton(_tenantStore);
        _provider = services.BuildServiceProvider();
    }

    [Fact]
    public void ClientFilter_StoresTenantAndUserOfEnqueuingScope()
    {
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(Tenant.ToTenantInfo(), Tenant.ConnectionString);
        scope.ServiceProvider.GetRequiredService<CurrentUser>().SetUser(UserId, Tenant.Id);
        AmbientServiceScope.Current = scope.ServiceProvider;

        var context = new CreatingContext(new CreateContext(
            Substitute.For<JobStorage>(), Substitute.For<IStorageConnection>(), Job.FromExpression<ProbeJob>(j => j.Run()), null));
        new TenantJobClientFilter().OnCreating(context);

        context.GetJobParameter<Guid>(TenantJobClientFilter.TenantParameter).ShouldBe(Tenant.Id);
        context.GetJobParameter<Guid>(TenantJobClientFilter.UserParameter).ShouldBe(UserId);
    }

    [Fact]
    public void ClientFilter_WithoutAmbientScope_AddsNothing()
    {
        AmbientServiceScope.Current = null;
        var context = new CreatingContext(new CreateContext(
            Substitute.For<JobStorage>(), Substitute.For<IStorageConnection>(), Job.FromExpression<ProbeJob>(j => j.Run()), null));

        new TenantJobClientFilter().OnCreating(context);

        context.Parameters.ShouldNotContainKey(TenantJobClientFilter.TenantParameter);
    }

    [Fact]
    public void Activator_RestoresTenant_AndRunsJobAsSystemOnBehalfOfUser()
    {
        var activator = new TenantAwareJobActivator(_provider.GetRequiredService<IServiceScopeFactory>());

        using var scope = activator.BeginScope(CreateActivatorContext(Tenant.Id, UserId));
        var job = (ProbeJob)scope.Resolve(typeof(ProbeJob));

        job.Tenant.RequiredTenant.Code.ShouldBe("alpha");
        job.User.IsSystem.ShouldBeTrue();
        job.User.UserId.ShouldBe(UserId);
        AmbientServiceScope.Current.ShouldNotBeNull();
    }

    [Fact]
    public void Activator_RefusesJobsOfInactiveTenants()
    {
        var inactive = Tenant with { Id = Guid.CreateVersion7(), Status = TenantStatus.Inactive };
        _tenantStore.FindByIdAsync(inactive.Id, Arg.Any<CancellationToken>()).Returns(inactive);
        var activator = new TenantAwareJobActivator(_provider.GetRequiredService<IServiceScopeFactory>());

        Should.Throw<InvalidOperationException>(() => activator.BeginScope(CreateActivatorContext(inactive.Id, null)));
    }

    public void Dispose()
    {
        AmbientServiceScope.Current = null;
        _provider.Dispose();
    }

    private static JobActivatorContext CreateActivatorContext(Guid tenantId, Guid? userId)
    {
        var connection = Substitute.For<IStorageConnection>();
        connection.GetJobParameter("job-1", TenantJobClientFilter.TenantParameter).Returns(SerializationHelper.Serialize(tenantId));
        connection.GetJobParameter("job-1", TenantJobClientFilter.UserParameter).Returns(userId is null ? null : SerializationHelper.Serialize(userId));
        var job = new BackgroundJob("job-1", Job.FromExpression<ProbeJob>(j => j.Run()), DateTime.UtcNow);
        return new JobActivatorContext(connection, job, Substitute.For<IJobCancellationToken>());
    }

    public sealed class ProbeJob(ITenantContext tenant, ICurrentUser user)
    {
        public ITenantContext Tenant => tenant;

        public ICurrentUser User => user;

        public string Run() => tenant.RequiredTenant.Code;
    }
}
