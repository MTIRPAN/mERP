using Merp.Application.Abstractions.Jobs;
using Merp.Application.Abstractions.Messaging;
using Merp.Modules.Recruitment.Application.CvAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Recruitment.Infrastructure.Applications;

/// <summary>
/// Every 2 minutes: analyzes the CVs of new applications with AI, at most five per tenant (A3, D51). Tenants with the
/// feature off are skipped; the monthly budget stops it like any other call.
/// </summary>
public sealed class ApplicationCvAnalysisJob(ITenantJobRunner tenants) : IRecurringJob
{
    public const string JobId = "recruitment.application-cv-analysis";
    public const string Cron = "*/2 * * * *";
    public const int PerRun = 5;

    public Task RunAsync(CancellationToken cancellationToken) =>
        tenants.RunForEachTenantAsync(
            async (services, ct) =>
            {
                var sender = services.GetRequiredService<ISender>();

                // One query per application: each AI call runs outside a database transaction, and its result is saved alone
                for (var i = 0; i < PerRun; i++)
                {
                    var result = await sender.Send(new AnalyzeNextApplicationCvQuery(), ct);
                    if (result.IsFailure)
                    {
                        throw new InvalidOperationException($"Analyzing the CVs of applications failed: {result.Error.Code}");
                    }

                    if (!result.Value)
                    {
                        return;
                    }
                }
            },
            cancellationToken);
}
