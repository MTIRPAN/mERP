using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using Merp.Application.Abstractions.AI;
using Microsoft.Extensions.Options;

namespace Merp.Infrastructure.AI;

/// <summary>
/// Claude through the official Anthropic SDK (D24): structured outputs for the result schema, adaptive thinking with
/// an explicit effort, prompt caching for the stable instructions and, optionally, server-side fallback when the
/// model declines for policy reasons.
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
        if (_client is null)
        {
            throw new AiProviderException("The Anthropic provider has no API key.");
        }

        var parameters = new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = request.MaxOutputTokens,
            System = new List<BetaTextBlockParam>
            {
                new() { Text = request.Instructions, CacheControl = new BetaCacheControlEphemeral() },
            },
            OutputConfig = new BetaOutputConfig
            {
                Effort = request.Effort switch
                {
                    AiEffort.Low => Effort.Low,
                    AiEffort.High => Effort.High,
                    _ => Effort.Medium,
                },
                Format = new BetaJsonOutputFormat { Schema = request.OutputSchema },
            },
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
            message = await _client.Beta.Messages.Create(parameters, cancellationToken);
        }
        catch (Exception exception) when (exception is AnthropicException or HttpRequestException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new AiProviderException($"The Anthropic request for '{request.Feature}' failed: {exception.GetType().Name}.", exception);
        }

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
