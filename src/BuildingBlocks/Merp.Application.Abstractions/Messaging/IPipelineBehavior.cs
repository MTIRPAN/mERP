using Merp.SharedKernel.Results;

namespace Merp.Application.Abstractions.Messaging;

/// <summary>Continuation to the next behavior or the handler.</summary>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>()
    where TResponse : Result;

/// <summary>
/// Cross-cutting step around request handling. Behaviors run in registration order:
/// Logging → Validation → Authorization → UnitOfWork (commands only) → handler.
/// </summary>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}
