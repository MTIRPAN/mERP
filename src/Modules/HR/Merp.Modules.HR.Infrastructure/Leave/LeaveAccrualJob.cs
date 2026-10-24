using Merp.Application.Abstractions.Jobs;
using Merp.Application.Abstractions.Messaging;
using Merp.Modules.HR.Application.Leave;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Merp.Modules.HR.Infrastructure.Leave;

/// <summary>
/// Every night at 01:00: writes the entitlements due to each employment, catching up missed days, and closes the
/// balances of employments that ended (docs/domain/izin-kurallari.md §5, D43). Each employment is its own command, so
/// one failing does not stop the others; the next run catches it up.
/// </summary>
public sealed partial class LeaveAccrualJob(ITenantJobRunner tenants, ILogger<LeaveAccrualJob> logger) : IRecurringJob
{
    public const string JobId = "hr.leave-accruals";

    public const string Cron = "0 1 * * *";

    public Task RunAsync(CancellationToken cancellationToken) =>
        tenants.RunForEachTenantAsync(
            async (services, ct) =>
            {
                var sender = services.GetRequiredService<ISender>();
                var candidates = await sender.Send(new GetLeaveAccrualCandidatesQuery(), ct);
                foreach (var employmentId in candidates.Value)
                {
                    var result = await sender.Send(new AccrueEmploymentLeaveCommand(employmentId), ct);
                    if (result.IsFailure)
                    {
                        LogFailed(logger, employmentId, result.Error.Code);
                    }
                }
            },
            cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Leave accrual of employment {EmploymentId} failed: {Code}")]
    private static partial void LogFailed(ILogger logger, Guid employmentId, string code);
}
