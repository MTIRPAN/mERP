using Hangfire;
using Hangfire.Common;
using Merp.Application.Abstractions.Jobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Merp.Web.Jobs;

/// <summary>
/// Schedules every <see cref="RecurringJobDefinition"/> on start. A storage outage only logs: the schedules are
/// stored, so the next start restores them.
/// </summary>
internal sealed partial class RecurringJobRegistrar(
    IEnumerable<RecurringJobDefinition> definitions,
    IRecurringJobManager manager,
    ILogger<RecurringJobRegistrar> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var definition in definitions)
        {
            if (!typeof(IRecurringJob).IsAssignableFrom(definition.JobType))
            {
                throw new InvalidOperationException($"{definition.JobType.FullName} does not implement {nameof(IRecurringJob)}.");
            }

            try
            {
                var method = typeof(IRecurringJob).GetMethod(nameof(IRecurringJob.RunAsync))!;
                // Hangfire passes the job's own cancellation token in place of this argument
                var job = new Job(definition.JobType, method, CancellationToken.None);
                manager.AddOrUpdate(definition.Id, job, definition.Cron, new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogRegistrationFailed(logger, exception, definition.Id);
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not schedule the recurring job {JobId}")]
    private static partial void LogRegistrationFailed(ILogger logger, Exception exception, string jobId);
}
