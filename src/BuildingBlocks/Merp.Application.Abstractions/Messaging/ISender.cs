using Merp.SharedKernel.Results;

namespace Merp.Application.Abstractions.Messaging;

/// <summary>Sends a request through the pipeline to its handler.</summary>
public interface ISender
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        where TResponse : Result;
}
