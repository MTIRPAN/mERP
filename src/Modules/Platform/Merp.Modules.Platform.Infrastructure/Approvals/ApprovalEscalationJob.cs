using Merp.Application.Abstractions.Jobs;
using Merp.Application.Abstractions.Messaging;
using Merp.Modules.Platform.Application.Approvals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Merp.Modules.Platform.Infrastructure.Approvals;

/// <summary>
/// Every 15 minutes: handles overdue approval tasks by their step's escalation (§2.5). One command per request, so
/// a request that fails (e.g. decided at the same moment) does not hold back the others; it is tried again next run.
/// </summary>
public sealed partial class ApprovalEscalationJob(ITenantJobRunner tenants, ILogger<ApprovalEscalationJob> logger) : IRecurringJob
{
    public const string JobId = "approvals.escalation";
    public const string Cron = "*/15 * * * *";

    public Task RunAsync(CancellationToken cancellationToken) =>
        tenants.RunForEachTenantAsync(
            async (services, ct) =>
            {
                var sender = services.GetRequiredService<ISender>();
                var overdue = await sender.Send(new GetOverdueApprovalRequestsQuery(), ct);
                if (overdue.IsFailure)
                {
                    throw new InvalidOperationException($"Listing overdue approvals failed: {overdue.Error.Code}");
                }

                foreach (var requestId in overdue.Value)
                {
                    var result = await sender.Send(new EscalateApprovalCommand(requestId), ct);
                    if (result.IsFailure)
                    {
                        LogEscalationFailed(logger, requestId, result.Error.Code);
                    }
                }
            },
            cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Escalating approval request {RequestId} failed: {Code}")]
    private static partial void LogEscalationFailed(ILogger logger, Guid requestId, string code);
}
