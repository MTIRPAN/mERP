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
    private readonly ConcurrentDictionary<string, IReadOnlyList<AiModelBatchItem>> _batches = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyList<AiModelBatchResult>> _batchResults = new(StringComparer.Ordinal);

    public bool IsConfigured { get; set; } = true;

    public IReadOnlyCollection<AiModelRequest> Requests => _requests;

    /// <summary>The batches sent, by id (D59).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<AiModelBatchItem>> Batches => _batches;

    /// <summary>Ends a batch: an output for each custom id, or null for a request that errored.</summary>
    public void EndsBatch<T>(string batchId, IReadOnlyDictionary<string, T?> outputs, long inputTokens = 4_000, long outputTokens = 1_500)
        where T : class =>
        _batchResults[batchId] =
        [
            .. outputs.Select(o => new AiModelBatchResult(
                o.Key,
                o.Value is null
                    ? null
                    : new AiModelResponse("claude-sonnet-5-5", AiStopReason.Completed, JsonSerializer.Serialize(o.Value, AiSchema.SerializerOptions), inputTokens, outputTokens, 0, 0))),
        ];

    /// <summary>Returns <paramref name="output"/> serialized the way the model would write it.</summary>
    public void Returns<T>(T output, long inputTokens = 4_000, long outputTokens = 1_500) =>
        _responses.Enqueue(_ => new AiModelResponse(
            "claude-sonnet-5-5",
            AiStopReason.Completed,
            JsonSerializer.Serialize(output, AiSchema.SerializerOptions),
            inputTokens,
            outputTokens,
            0,
            0));

    public void Stops(AiStopReason stopReason, string text = "", long inputTokens = 4_000, long outputTokens = 0) =>
        _responses.Enqueue(_ => new AiModelResponse("claude-sonnet-5-5", stopReason, text, inputTokens, outputTokens, 0, 0));

    public void Fails() =>
        _responses.Enqueue(_ => throw new AiProviderException("Fake provider failure."));

    public void Reset()
    {
        _responses.Clear();
        _requests.Clear();
        _batches.Clear();
        _batchResults.Clear();
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

    public Task<string> SubmitBatchAsync(IReadOnlyList<AiModelBatchItem> items, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        var id = $"msgbatch_{Guid.NewGuid():N}";
        _batches[id] = items;
        return Task.FromResult(id);
    }

    public Task<IReadOnlyList<AiModelBatchResult>?> GetBatchResultsAsync(string batchId, CancellationToken cancellationToken) =>
        _batches.ContainsKey(batchId)
            ? Task.FromResult(_batchResults.TryGetValue(batchId, out var results) ? results : null)
            : throw new AiProviderException($"Unknown batch {batchId}.");
}
