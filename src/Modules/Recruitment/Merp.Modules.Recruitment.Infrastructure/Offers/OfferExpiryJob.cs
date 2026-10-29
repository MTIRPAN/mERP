using Merp.Application.Abstractions.Jobs;
using Merp.Application.Abstractions.Messaging;
using Merp.Modules.Recruitment.Application.Offers;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Recruitment.Infrastructure.Offers;

/// <summary>Every hour: closes the sent offers the candidates did not answer by their last day (D53).</summary>
public sealed class OfferExpiryJob(ITenantJobRunner tenants) : IRecurringJob
{
    public const string JobId = "recruitment.offer-expiry";
    public const string Cron = "15 * * * *";

    public Task RunAsync(CancellationToken cancellationToken) =>
        tenants.RunForEachTenantAsync(
            async (services, ct) =>
            {
                var result = await services.GetRequiredService<ISender>().Send(new ExpireOffersCommand(), ct);
                if (result.IsFailure)
                {
                    throw new InvalidOperationException($"Closing the expired offers failed: {result.Error.Code}");
                }
            },
            cancellationToken);
}
