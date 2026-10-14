using System.Linq.Expressions;
using Hangfire;
using Merp.Application.Abstractions.Jobs;
using Merp.Application.Abstractions.Messaging;

namespace Merp.Infrastructure.Jobs;

/// <summary>
/// Hangfire jobs enqueued after the commit, so a rolled back command starts no work. The tenant and user travel with
/// the job (<see cref="TenantJobClientFilter"/>).
/// </summary>
public sealed class HangfireBackgroundTasks(IBackgroundJobClient client, IAfterCommit afterCommit) : IBackgroundTasks
{
    public void Enqueue<TService>(Expression<Func<TService, Task>> work)
        where TService : notnull =>
        afterCommit.Enqueue(_ =>
        {
            client.Enqueue(work);
            return Task.CompletedTask;
        });
}
