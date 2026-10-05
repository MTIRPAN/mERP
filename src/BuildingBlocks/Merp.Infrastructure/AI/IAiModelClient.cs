using System.Text.Json;
using Merp.Application.Abstractions.AI;

namespace Merp.Infrastructure.AI;

/// <summary>Provider adapter used by <see cref="AiService"/>. Tests replace it with a fake.</summary>
public interface IAiModelClient
{
    /// <summary>False when the provider has no credentials; every feature is then disabled.</summary>
    bool IsConfigured { get; }

    /// <exception cref="AiProviderException">The provider failed, timed out or rejected the request.</exception>
    Task<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken);
}

/// <summary>One provider call; <c>OutputSchema</c> is the JSON schema the output must follow (structured outputs).</summary>
public sealed record AiModelRequest(
    string Feature,
    string Instructions,
    string Input,
    IReadOnlyDictionary<string, JsonElement> OutputSchema,
    AiEffort Effort,
    int MaxOutputTokens);

public sealed record AiModelResponse(
    string Model,
    AiStopReason StopReason,
    string Text,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens);

public enum AiStopReason
{
    Completed,
    MaxTokens,
    Refusal,
    Other,
}

/// <summary>The provider could not produce a response; the message never contains request content.</summary>
public sealed class AiProviderException : Exception
{
    public AiProviderException()
    {
    }

    public AiProviderException(string message)
        : base(message)
    {
    }

    public AiProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
