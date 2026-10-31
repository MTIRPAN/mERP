using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using Anthropic.Models.Beta.Messages.Batches;
using Merp.Application.Abstractions.AI;
using Microsoft.Extensions.Options;

namespace Merp.Infrastructure.AI;

/// <summary>
/// Claude through the official Anthropic SDK (D24): structured outputs for the result schema, adaptive thinking with
/// an explicit effort, prompt caching for the stable instructions and, optionally, server-side fallback when the
/// model declines for policy reasons. Batches (D59) go through the Message Batches API with the same request shape,
/// without the fallback.
/// </summary>
internal sealed class AnthropicModelClient : IAiModelClient, IDisposable
{
    private readonly AiOptions _options;
    private readonly AnthropicClient? _client;

    public AnthropicModelClient(IOptions<AiOptions> options, IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.Anthropic.ApiKey))
        {
            return;
        }

        // Key and address come only from configuration: the SDK's environment variable defaults are not used.
        _client = new AnthropicClient
        {
            ApiKey = _options.Anthropic.ApiKey,
            BaseUrl = _options.Anthropic.BaseUrl.ToString().TrimEnd('/'),
            HttpClient = httpClientFactory.CreateClient(HttpClientName),
            MaxRetries = _options.MaxRetries,
            Timeout = _options.RequestTimeout,
        };
    }

    public const string HttpClientName = "anthropic";

    public bool IsConfigured => _client is not null;

    public void Dispose() => _client?.Dispose();

    public async Task<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = Client();
        var parameters = new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = request.MaxOutputTokens,
            System = Instructions(request),
            OutputConfig = OutputConfig(request),
            Messages = [new() { Role = Role.User, Content = request.Input }],
        };

        if (_options.Anthropic.ServerSideFallback)
        {
            parameters = parameters with
            {
                Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
                Fallbacks = new Default(),
            };
        }

        if (!string.IsNullOrWhiteSpace(_options.Anthropic.InferenceGeo))
        {
            parameters = parameters with { InferenceGeo = _options.Anthropic.InferenceGeo };
        }

        BetaMessage message;
        try
        {
            message = await client.Beta.Messages.Create(parameters, cancellationToken);
        }
        catch (Exception exception) when (IsProviderFailure(exception, cancellationToken))
        {
            throw new AiProviderException($"The Anthropic request for '{request.Feature}' failed: {exception.GetType().Name}.", exception);
        }

        return Response(message);
    }

    public async Task<string> SubmitBatchAsync(IReadOnlyList<AiModelBatchItem> items, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        var client = Client();
        var parameters = new BatchCreateParams
        {
            Requests = [.. items.Select(item => new Request { CustomID = item.CustomId, Params = BatchParams(item.Request) })],
        };

        try
        {
            return (await client.Beta.Messages.Batches.Create(parameters, cancellationToken)).ID;
        }
        catch (Exception exception) when (IsProviderFailure(exception, cancellationToken))
        {
            throw new AiProviderException($"The Anthropic batch of {items.Count} requests failed: {exception.GetType().Name}.", exception);
        }
    }

    public async Task<IReadOnlyList<AiModelBatchResult>?> GetBatchResultsAsync(string batchId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        var client = Client();
        try
        {
            var batch = await client.Beta.Messages.Batches.Retrieve(batchId, cancellationToken: cancellationToken);
            if (!string.Equals(batch.ProcessingStatus.Raw(), "ended", StringComparison.Ordinal))
            {
                return null;
            }

            var results = new List<AiModelBatchResult>();
            await foreach (var item in client.Beta.Messages.Batches.ResultsStreaming(batchId, cancellationToken: cancellationToken))
            {
                results.Add(new AiModelBatchResult(item.CustomID, item.Result.TryPickSucceeded(out var succeeded) ? Response(succeeded.Message) : null));
            }

            return results;
        }
        catch (Exception exception) when (IsProviderFailure(exception, cancellationToken))
        {
            throw new AiProviderException($"Reading the Anthropic batch {batchId} failed: {exception.GetType().Name}.", exception);
        }
    }

    private AnthropicClient Client() => _client ?? throw new AiProviderException("The Anthropic provider has no API key.");

    private static bool IsProviderFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is AnthropicException or HttpRequestException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private static List<BetaTextBlockParam> Instructions(AiModelRequest request) =>
        [new() { Text = request.Instructions, CacheControl = new BetaCacheControlEphemeral() }];

    private static BetaOutputConfig OutputConfig(AiModelRequest request) => new()
    {
        Effort = request.Effort switch
        {
            AiEffort.Low => Effort.Low,
            AiEffort.High => Effort.High,
            _ => Effort.Medium,
        },
        Format = new BetaJsonOutputFormat { Schema = request.OutputSchema },
    };

    private Params BatchParams(AiModelRequest request)
    {
        var parameters = new Params
        {
            Model = _options.Model,
            MaxTokens = request.MaxOutputTokens,
            System = Instructions(request),
            OutputConfig = OutputConfig(request),
            Messages = [new() { Role = Role.User, Content = request.Input }],
        };

        return string.IsNullOrWhiteSpace(_options.Anthropic.InferenceGeo)
            ? parameters
            : parameters with { InferenceGeo = _options.Anthropic.InferenceGeo };
    }

    private static AiModelResponse Response(BetaMessage message)
    {
        var text = string.Concat(message.Content
            .Select(block => block.TryPickText(out var textBlock) ? textBlock.Text : null)
            .Where(t => t is not null));

        var stopReason = message.StopReason?.Raw() switch
        {
            "end_turn" => AiStopReason.Completed,
            "max_tokens" => AiStopReason.MaxTokens,
            "refusal" => AiStopReason.Refusal,
            _ => AiStopReason.Other,
        };

        return new AiModelResponse(
            message.Model.Raw(),
            stopReason,
            text,
            message.Usage.InputTokens,
            message.Usage.OutputTokens,
            message.Usage.CacheReadInputTokens ?? 0,
            message.Usage.CacheCreationInputTokens ?? 0);
    }
}
