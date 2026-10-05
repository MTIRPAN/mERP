using System.Collections.Concurrent;
using System.Text.Json;
using Merp.Infrastructure.AI;

namespace Merp.Testing.AI;

/// <summary>
/// AI provider for tests: returns arranged responses in order and records the requests. No test calls the real
/// API (D24); prompts are evaluated separately with an eval set.
/// </summary>
public sealed class FakeAiModelClient : IAiModelClient
{
    private readonly ConcurrentQueue<Func<AiModelRequest, AiModelResponse>> _responses = new();
    private readonly ConcurrentQueue<AiModelRequest> _requests = new();

    public bool IsConfigured { get; set; } = true;

    public IReadOnlyCollection<AiModelRequest> Requests => _requests;

    /// <summary>Returns <paramref name="output"/> serialized the way the model would write it.</summary>
    public void Returns<T>(T output, long inputTokens = 4_000, long outputTokens = 1_500) =>
        _responses.Enqueue(_ => new AiModelResponse(
            "claude-opus-5-5",
            AiStopReason.Completed,
            JsonSerializer.Serialize(output, AiSchema.SerializerOptions),
            inputTokens,
            outputTokens,
            0,
            0));

    public void Stops(AiStopReason stopReason, string text = "", long inputTokens = 4_000, long outputTokens = 0) =>
        _responses.Enqueue(_ => new AiModelResponse("claude-opus-5-5", stopReason, text, inputTokens, outputTokens, 0, 0));

    public void Fails() =>
        _responses.Enqueue(_ => throw new AiProviderException("Fake provider failure."));

    public void Reset()
    {
        _responses.Clear();
        _requests.Clear();
        IsConfigured = true;
    }

    public Task<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        _requests.Enqueue(request);
        return _responses.TryDequeue(out var respond)
            ? Task.FromResult(respond(request))
            : throw new InvalidOperationException($"No fake AI response is arranged for '{request.Feature}'.");
    }
}
