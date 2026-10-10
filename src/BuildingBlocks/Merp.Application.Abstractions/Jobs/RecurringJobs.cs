namespace Merp.Application.Abstractions.Jobs;

/// <summary>A job the host runs on a schedule. It runs as system without a tenant; use <see cref="ITenantJobRunner"/> to visit tenants.</summary>
public interface IRecurringJob
{
    Task RunAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Registers <paramref name="JobType"/> with the job server under <paramref name="Id"/> (only on hosts that run the
/// job server, <c>Jobs:EnableServer</c>).
/// </summary>
/// <param name="Id">Stable id, e.g. <c>files.sweep</c>; changing it leaves the old schedule behind.</param>
/// <param name="Cron">Cron expression in UTC, e.g. <c>0 2 * * *</c>.</param>
/// <param name="JobType">An <see cref="IRecurringJob"/> resolvable from DI.</param>
public sealed record RecurringJobDefinition(string Id, string Cron, Type JobType)
{
    public static RecurringJobDefinition For<TJob>(string id, string cron)
        where TJob : IRecurringJob =>
        new(id, cron, typeof(TJob));
}
