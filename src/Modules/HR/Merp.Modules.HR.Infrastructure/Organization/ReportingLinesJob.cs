using Merp.Application.Abstractions.Jobs;
using Merp.Application.Abstractions.Messaging;
using Merp.Modules.HR.Application.Organization;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.HR.Infrastructure.Organization;

/// <summary>
/// Every night: rebuilds the reporting lines of every company in every tenant, whatever the event handlers missed
/// (D37).
/// </summary>
public sealed class ReportingLinesJob(ITenantJobRunner tenants) : IRecurringJob
{
    public const string JobId = "hr.reporting-lines";

    /// <summary>00:30 UTC (03:30 in Türkiye).</summary>
    public const string Cron = "30 0 * * *";

    public Task RunAsync(CancellationToken cancellationToken) =>
        tenants.RunForEachTenantAsync(
            async (services, ct) =>
            {
                var result = await services.GetRequiredService<ISender>().Send(new RebuildReportingLinesCommand(), ct);
                if (result.IsFailure)
                {
                    throw new InvalidOperationException($"Rebuilding the reporting lines failed: {result.Error.Code}");
                }
            },
            cancellationToken);
}
