using Merp.Application.Abstractions.Jobs;
using Merp.Application.Abstractions.Messaging;
using Merp.Modules.HR.Application.Employees;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.HR.Infrastructure.Employees;

/// <summary>
/// Every hour: announces the assignments whose first day has come in their company's time zone (D38). Hourly, so a
/// company in any time zone hears of the change within an hour of its day starting.
/// </summary>
public sealed class AssignmentsEffectiveJob(ITenantJobRunner tenants) : IRecurringJob
{
    public const string JobId = "hr.assignments-effective";

    public const string Cron = "5 * * * *";

    public Task RunAsync(CancellationToken cancellationToken) =>
        tenants.RunForEachTenantAsync(
            async (services, ct) =>
            {
                var result = await services.GetRequiredService<ISender>().Send(new AnnounceStartedAssignmentsCommand(), ct);
                if (result.IsFailure)
                {
                    throw new InvalidOperationException($"Announcing the started assignments failed: {result.Error.Code}");
                }
            },
            cancellationToken);
}
