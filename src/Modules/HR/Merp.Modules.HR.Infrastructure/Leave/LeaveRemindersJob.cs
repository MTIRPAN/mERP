using Merp.Application.Abstractions.Jobs;
using Merp.Application.Abstractions.Messaging;
using Merp.Modules.HR.Application.Leave;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.HR.Infrastructure.Leave;

/// <summary>Every morning: reminds the employee and the manager of the approved leaves that start tomorrow (D44).</summary>
public sealed class LeaveRemindersJob(ITenantJobRunner tenants) : IRecurringJob
{
    public const string JobId = "hr.leave-reminders";

    /// <summary>06:00 UTC, 09:00 in Istanbul: at the start of the working day before the leave.</summary>
    public const string Cron = "0 6 * * *";

    public Task RunAsync(CancellationToken cancellationToken) =>
        tenants.RunForEachTenantAsync(
            async (services, ct) =>
            {
                var result = await services.GetRequiredService<ISender>().Send(new SendLeaveRemindersCommand(), ct);
                if (result.IsFailure)
                {
                    throw new InvalidOperationException($"Sending the leave reminders failed: {result.Error.Code}");
                }
            },
            cancellationToken);
}
