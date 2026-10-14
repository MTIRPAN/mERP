using System.Linq.Expressions;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Jobs;
using Merp.Application.Abstractions.Messaging;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Testing.Web;

/// <summary>
/// Runs background work right after the commit, before the request ends, as Hangfire would: in its own scope, as
/// the system on behalf of the caller (<c>TenantAwareJobActivator</c>). Tests set <c>Jobs:EnableServer=false</c>.
/// </summary>
public sealed class InlineBackgroundTasks(
    IServiceScopeFactory scopeFactory,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IAfterCommit afterCommit) : IBackgroundTasks
{
    public void Enqueue<TService>(Expression<Func<TService, Task>> work)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(work);
        var tenantId = tenantContext.Tenant?.Id;
        var userId = currentUser.UserId;
        var run = work.Compile();
        afterCommit.Enqueue(async cancellationToken =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            if (tenantId is { } id)
            {
                var tenant = (await scope.ServiceProvider.GetRequiredService<ITenantStore>().FindByIdAsync(id, cancellationToken))!;
                scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant.ToTenantInfo(), tenant.ConnectionString);
            }

            scope.ServiceProvider.GetRequiredService<CurrentUser>().SetSystem(tenantId, userId);
            await run(scope.ServiceProvider.GetRequiredService<TService>());
        });
    }
}
