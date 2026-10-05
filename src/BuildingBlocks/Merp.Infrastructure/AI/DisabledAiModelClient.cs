namespace Merp.Infrastructure.AI;

/// <summary>Used when <see cref="AiOptions.Provider"/> is <see cref="AiProvider.None"/>.</summary>
internal sealed class DisabledAiModelClient : IAiModelClient
{
    public bool IsConfigured => false;

    public Task<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken) =>
        throw new AiProviderException("No AI provider is configured.");
}
