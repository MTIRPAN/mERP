using Merp.Application.Abstractions.Jobs;
using Merp.Application.Abstractions.Messaging;
using Merp.Modules.Recruitment.Application.Interviews;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Recruitment.Infrastructure.Interviews;

/// <summary>Every 5 minutes: reminds the candidate and the interviewers a day and an hour before an interview (D52).</summary>
public sealed class InterviewRemindersJob(ITenantJobRunner tenants) : IRecurringJob
{
    public const string JobId = "recruitment.interview-reminders";
    public const string Cron = "*/5 * * * *";

    public Task RunAsync(CancellationToken cancellationToken) =>
        tenants.RunForEachTenantAsync(
            async (services, ct) =>
            {
                var result = await services.GetRequiredService<ISender>().Send(new SendInterviewRemindersCommand(), ct);
                if (result.IsFailure)
                {
                    throw new InvalidOperationException($"Sending the interview reminders failed: {result.Error.Code}");
                }
            },
            cancellationToken);
}
