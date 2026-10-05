using System.Diagnostics;
using System.Text.Json;
using Merp.Application.Abstractions.AI;
using Merp.Application.Abstractions.Context;
using Merp.SharedKernel.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Merp.Infrastructure.AI;

/// <summary>
/// Gatekeeper for every AI call (D24): checks that the feature is enabled for the tenant and the monthly budget is not
/// used up, calls the provider with the result type's JSON schema, and records tokens and estimated cost whatever the
/// outcome. Prompts and outputs are never logged.
/// </summary>
internal sealed partial class AiService(
    IAiModelClient client,
    IOptionsMonitor<AiOptions> options,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IAiUsageLedger ledger,
    AiTelemetry telemetry,
    TimeProvider timeProvider,
    ILogger<AiService> logger) : IAiService
{
    public bool IsEnabled(string feature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        return options.CurrentValue.Provider != AiProvider.None
            && client.IsConfigured
            && tenantContext.Tenant is { } tenant
            && options.CurrentValue.ForTenant(tenant.Code).Allows(feature);
    }

    public async Task<Result<AiResponse<T>>> GenerateAsync<T>(AiRequest request, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsEnabled(request.Feature))
        {
            return AiErrors.Disabled;
        }

        var current = options.CurrentValue;
        var settings = current.ForTenant(tenantContext.RequiredTenant.Code);
        if (settings.MonthlyBudgetUsd > 0
            && await ledger.GetCostSinceAsync(AiBudgetPeriod.CurrentStart(timeProvider.GetUtcNow()), cancellationToken) >= settings.MonthlyBudgetUsd)
        {
            LogBudgetExceeded(logger, request.Feature, settings.MonthlyBudgetUsd);
            return AiErrors.BudgetExceeded;
        }

        var modelRequest = new AiModelRequest(
            request.Feature,
            request.Instructions,
            request.Input,
            AiSchema.For(typeof(T)),
            request.Effort,
            request.MaxOutputTokens ?? current.MaxOutputTokens);

        var started = Stopwatch.GetTimestamp();
        AiModelResponse response;
        try
        {
            response = await client.CompleteAsync(modelRequest, cancellationToken);
        }
        catch (AiProviderException exception)
        {
            LogProviderFailed(logger, exception, request.Feature);
            await RecordAsync(request, current.Model, AiUsageOutcome.Failed, null, started, current.Pricing);
            return AiErrors.Unavailable;
        }

        switch (response.StopReason)
        {
            case AiStopReason.Refusal:
                await RecordAsync(request, response.Model, AiUsageOutcome.Refused, response, started, current.Pricing);
                return AiErrors.Refused;
            case AiStopReason.MaxTokens:
                await RecordAsync(request, response.Model, AiUsageOutcome.Truncated, response, started, current.Pricing);
                return AiErrors.InvalidOutput;
        }

        T? value;
        try
        {
            value = JsonSerializer.Deserialize<T>(response.Text, AiSchema.SerializerOptions);
        }
        catch (JsonException exception)
        {
            LogInvalidOutput(logger, exception, request.Feature);
            value = null;
        }

        if (value is null)
        {
            await RecordAsync(request, response.Model, AiUsageOutcome.InvalidOutput, response, started, current.Pricing);
            return AiErrors.InvalidOutput;
        }

        var usage = await RecordAsync(request, response.Model, AiUsageOutcome.Succeeded, response, started, current.Pricing);
        return new AiResponse<T>(value, usage);
    }

    private async Task<AiUsage> RecordAsync(
        AiRequest request,
        string model,
        AiUsageOutcome outcome,
        AiModelResponse? response,
        long started,
        AiPricingOptions pricing)
    {
        var duration = Stopwatch.GetElapsedTime(started);
        var cost = response is null
            ? 0m
            : pricing.Cost(response.InputTokens, response.OutputTokens, response.CacheReadTokens, response.CacheWriteTokens);

        var entry = new AiUsageEntry(
            timeProvider.GetUtcNow(),
            request.Feature,
            request.PromptVersion,
            model,
            outcome,
            currentUser.UserId,
            response?.InputTokens ?? 0,
            response?.OutputTokens ?? 0,
            response?.CacheReadTokens ?? 0,
            response?.CacheWriteTokens ?? 0,
            cost,
            (int)Math.Min(int.MaxValue, duration.TotalMilliseconds),
            request.EntityType,
            request.EntityId,
            Activity.Current?.TraceId.ToString());

        telemetry.Record(entry);
        LogCompleted(logger, request.Feature, outcome, entry.InputTokens, entry.OutputTokens, cost, entry.DurationMs);

        // The call is billed even if the request is aborted now, so the record is written regardless.
        await ledger.RecordAsync(entry, CancellationToken.None);

        return new AiUsage(model, entry.InputTokens, entry.OutputTokens, entry.CacheReadTokens, entry.CacheWriteTokens, cost, duration);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "AI call for {Feature} finished with {Outcome}: {InputTokens} input / {OutputTokens} output tokens, ~{CostUsd} USD, {DurationMs} ms")]
    private static partial void LogCompleted(ILogger logger, string feature, AiUsageOutcome outcome, long inputTokens, long outputTokens, decimal costUsd, int durationMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI call for {Feature} rejected: the monthly budget of {BudgetUsd} USD is used up")]
    private static partial void LogBudgetExceeded(ILogger logger, string feature, decimal budgetUsd);

    [LoggerMessage(Level = LogLevel.Error, Message = "AI provider failed for {Feature}")]
    private static partial void LogProviderFailed(ILogger logger, Exception exception, string feature);

    [LoggerMessage(Level = LogLevel.Error, Message = "AI output for {Feature} could not be deserialized")]
    private static partial void LogInvalidOutput(ILogger logger, Exception exception, string feature);
}
