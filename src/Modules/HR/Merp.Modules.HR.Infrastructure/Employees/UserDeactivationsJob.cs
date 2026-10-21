using Merp.Application.Abstractions.Jobs;
using Merp.Application.Abstractions.Messaging;
using Merp.Modules.HR.Application.Employees;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.HR.Infrastructure.Employees;

/// <summary>
/// Every hour: asks Platform to deactivate the users of people whose last employment ended before the day of its
/// company (D39). Hourly, so a company in any time zone gets it within an hour of the day after the termination date.
/// </summary>
public sealed class UserDeactivationsJob(ITenantJobRunner tenants) : IRecurringJob
{
    public const string JobId = "hr.user-deactivations";

    public const string Cron = "15 * * * *";

    public Task RunAsync(CancellationToken cancellationToken) =>
        tenants.RunForEachTenantAsync(
            async (services, ct) =>
            {
                var result = await services.GetRequiredService<ISender>().Send(new RequestUserDeactivationsCommand(), ct);
                if (result.IsFailure)
                {
                    throw new InvalidOperationException($"Asking for the user deactivations failed: {result.Error.Code}");
                }
            },
            cancellationToken);
}
