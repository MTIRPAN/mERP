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
/// outcome. Prompts and outputs are never logged. Batches (D59) are estimated against the budget before they are sent
/// and recorded at the batch price when their results are collected.
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
    // Turkish text runs at about three characters a token; the estimate leans high so budgets are not overrun
    private const int CharactersPerToken = 3;

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
        if (settings.MonthlyBudgetUsd > 0 && await SpentAsync(cancellationToken) >= settings.MonthlyBudgetUsd)
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
            await RecordAsync(Call.Of(request), current.Model, AiUsageOutcome.Failed, null, Stopwatch.GetElapsedTime(started), priceFactor: 1m);
            return AiErrors.Unavailable;
        }

        return await ReadAsync<T>(Call.Of(request), response, Stopwatch.GetElapsedTime(started), priceFactor: 1m);
    }

    public async Task<Result<AiBatch>> SubmitBatchAsync<T>(AiBatchRequest request, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Items.Count is 0 or > AiBatchRequest.MaxItems)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Items.Count, $"A batch takes 1 to {AiBatchRequest.MaxItems} inputs.");
        }

        if (!IsEnabled(request.Feature))
        {
            return AiErrors.Disabled;
        }

        var current = options.CurrentValue;
        var settings = current.ForTenant(tenantContext.RequiredTenant.Code);
        var pricing = current.PricingFor(current.Model);
        var estimate = request.Items.Sum(item => pricing.Cost(
            (request.Instructions.Length + item.Input.Length) / CharactersPerToken,
            request.ExpectedOutputTokens,
            0,
            0)) * current.BatchPriceFactor;
        if (settings.MonthlyBudgetUsd > 0 && await SpentAsync(cancellationToken) + estimate > settings.MonthlyBudgetUsd)
        {
            LogBatchOverBudget(logger, request.Feature, request.Items.Count, estimate, settings.MonthlyBudgetUsd);
            return AiErrors.BudgetExceeded;
        }

        var schema = AiSchema.For(typeof(T));
        var maxOutputTokens = request.MaxOutputTokens ?? current.MaxOutputTokens;
        string id;
        try
        {
            id = await client.SubmitBatchAsync(
                [.. request.Items.Select(item => new AiModelBatchItem(item.CustomId, new AiModelRequest(request.Feature, request.Instructions, item.Input, schema, request.Effort, maxOutputTokens)))],
                cancellationToken);
        }
        catch (AiProviderException exception)
        {
            LogProviderFailed(logger, exception, request.Feature);
            return AiErrors.Unavailable;
        }

        LogBatchSubmitted(logger, request.Feature, request.Items.Count, estimate);
        return new AiBatch(id, request.Feature, request.PromptVersion, request.EntityType, Math.Round(estimate, 4));
    }

    public async Task<Result<IReadOnlyList<AiBatchResult<T>>?>> GetBatchResultsAsync<T>(AiBatch batch, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(batch);
        IReadOnlyList<AiModelBatchResult>? results;
        try
        {
            results = await client.GetBatchResultsAsync(batch.Id, cancellationToken);
        }
        catch (AiProviderException exception)
        {
            LogProviderFailed(logger, exception, batch.Feature);
            return AiErrors.Unavailable;
        }

        if (results is null)
        {
            return Result.Success<IReadOnlyList<AiBatchResult<T>>?>(null);
        }

        var model = options.CurrentValue.Model;
        var factor = options.CurrentValue.BatchPriceFactor;
        var read = new List<AiBatchResult<T>>(results.Count);
        foreach (var result in results)
        {
            var call = new Call(batch.Feature, batch.PromptVersion, batch.EntityType, result.CustomId);
            if (result.Response is not { } response)
            {
                // Errored, expired or canceled requests are not billed
                await RecordAsync(call, model, AiUsageOutcome.Failed, null, TimeSpan.Zero, factor);
                read.Add(new AiBatchResult<T>(result.CustomId, AiErrors.Unavailable));
                continue;
            }

            read.Add(new AiBatchResult<T>(result.CustomId, await ReadAsync<T>(call, response, TimeSpan.Zero, factor)));
        }

        return read;
    }

    private Task<decimal> SpentAsync(CancellationToken cancellationToken) =>
        ledger.GetCostSinceAsync(AiBudgetPeriod.CurrentStart(timeProvider.GetUtcNow()), cancellationToken);

    /// <summary>Turns a provider response into the result type, recording the call whatever the outcome.</summary>
    private async Task<Result<AiResponse<T>>> ReadAsync<T>(Call call, AiModelResponse response, TimeSpan duration, decimal priceFactor)
        where T : class
    {
        switch (response.StopReason)
        {
            case AiStopReason.Refusal:
                await RecordAsync(call, response.Model, AiUsageOutcome.Refused, response, duration, priceFactor);
                return AiErrors.Refused;
            case AiStopReason.MaxTokens:
                await RecordAsync(call, response.Model, AiUsageOutcome.Truncated, response, duration, priceFactor);
                return AiErrors.InvalidOutput;
        }

        T? value;
        try
        {
            value = JsonSerializer.Deserialize<T>(response.Text, AiSchema.SerializerOptions);
        }
        catch (JsonException exception)
        {
            LogInvalidOutput(logger, exception, call.Feature);
            value = null;
        }

        if (value is null)
        {
            await RecordAsync(call, response.Model, AiUsageOutcome.InvalidOutput, response, duration, priceFactor);
            return AiErrors.InvalidOutput;
        }

        var usage = await RecordAsync(call, response.Model, AiUsageOutcome.Succeeded, response, duration, priceFactor);
        return new AiResponse<T>(value, usage);
    }

    private async Task<AiUsage> RecordAsync(
        Call call,
        string model,
        AiUsageOutcome outcome,
        AiModelResponse? response,
        TimeSpan duration,
        decimal priceFactor)
    {
        var cost = response is null
            ? 0m
            : options.CurrentValue.PricingFor(model).Cost(response.InputTokens, response.OutputTokens, response.CacheReadTokens, response.CacheWriteTokens) * priceFactor;

        var entry = new AiUsageEntry(
            timeProvider.GetUtcNow(),
            call.Feature,
            call.PromptVersion,
            model,
            outcome,
            currentUser.UserId,
            response?.InputTokens ?? 0,
            response?.OutputTokens ?? 0,
            response?.CacheReadTokens ?? 0,
            response?.CacheWriteTokens ?? 0,
            cost,
            (int)Math.Min(int.MaxValue, duration.TotalMilliseconds),
            call.EntityType,
            call.EntityId,
            Activity.Current?.TraceId.ToString());

        telemetry.Record(entry);
        LogCompleted(logger, call.Feature, outcome, entry.InputTokens, entry.OutputTokens, cost, entry.DurationMs);

        // The call is billed even if the request is aborted now, so the record is written regardless.
        await ledger.RecordAsync(entry, CancellationToken.None);

        return new AiUsage(model, entry.InputTokens, entry.OutputTokens, entry.CacheReadTokens, entry.CacheWriteTokens, cost, duration);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "AI call for {Feature} finished with {Outcome}: {InputTokens} input / {OutputTokens} output tokens, ~{CostUsd} USD, {DurationMs} ms")]
    private static partial void LogCompleted(ILogger logger, string feature, AiUsageOutcome outcome, long inputTokens, long outputTokens, decimal costUsd, int durationMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI call for {Feature} rejected: the monthly budget of {BudgetUsd} USD is used up")]
    private static partial void LogBudgetExceeded(ILogger logger, string feature, decimal budgetUsd);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI batch for {Feature} with {Count} inputs held: ~{EstimateUsd} USD does not fit in the monthly budget of {BudgetUsd} USD")]
    private static partial void LogBatchOverBudget(ILogger logger, string feature, int count, decimal estimateUsd, decimal budgetUsd);

    [LoggerMessage(Level = LogLevel.Information, Message = "AI batch for {Feature} submitted with {Count} inputs, ~{EstimateUsd} USD")]
    private static partial void LogBatchSubmitted(ILogger logger, string feature, int count, decimal estimateUsd);

    [LoggerMessage(Level = LogLevel.Error, Message = "AI provider failed for {Feature}")]
    private static partial void LogProviderFailed(ILogger logger, Exception exception, string feature);

    [LoggerMessage(Level = LogLevel.Error, Message = "AI output for {Feature} could not be deserialized")]
    private static partial void LogInvalidOutput(ILogger logger, Exception exception, string feature);

    /// <summary>What a usage record says about a call.</summary>
    private sealed record Call(string Feature, string PromptVersion, string? EntityType, string? EntityId)
    {
        public static Call Of(AiRequest request) => new(request.Feature, request.PromptVersion, request.EntityType, request.EntityId);
    }
}
