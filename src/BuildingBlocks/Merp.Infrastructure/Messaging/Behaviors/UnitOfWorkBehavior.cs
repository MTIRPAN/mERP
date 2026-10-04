using Merp.Application.Abstractions.Messaging;
using Merp.SharedKernel.Results;

namespace Merp.Infrastructure.Messaging.Behaviors;

/// <summary>
/// Wraps commands in the module's transaction. A failed result or an exception rolls back, so neither business
/// data nor outbox messages are persisted. Queries pass through.
/// </summary>
internal sealed class UnitOfWorkBehavior<TRequest, TResponse>(UnitOfWorkRegistry registry, IServiceProvider serviceProvider)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (request is not IBaseCommand)
        {
            return await next();
        }

        var unitOfWork = registry.Resolve(typeof(TRequest), serviceProvider);
        await unitOfWork.BeginAsync(cancellationToken);

        TResponse response;
        try
        {
            response = await next();
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }

        if (response.IsFailure)
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            return response;
        }

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }

        return response;
    }
}
