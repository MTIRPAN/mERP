using Merp.Application.Abstractions.Messaging;
using Merp.SharedKernel.Results;

namespace Merp.Infrastructure.Messaging.Behaviors;

/// <summary>
/// Wraps commands in the module's transaction. A failed result or an exception rolls back, so neither business
/// data nor outbox messages are persisted. Queries pass through, and a command sent while the same module's unit
/// of work is already open joins it.
/// </summary>
internal sealed class UnitOfWorkBehavior<TRequest, TResponse>(
    UnitOfWorkRegistry registry,
    UnitOfWorkAccessor accessor,
    IServiceProvider serviceProvider)
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
        if (accessor.IsActive(unitOfWork))
        {
            return await next();
        }

        await unitOfWork.BeginAsync(cancellationToken);
        accessor.Push(unitOfWork);
        try
        {
            var response = await next();
            if (response.IsFailure)
            {
                await unitOfWork.RollbackAsync(CancellationToken.None);
                return response;
            }

            var commit = await unitOfWork.CommitAsync(cancellationToken);
            if (commit.IsFailure)
            {
                await unitOfWork.RollbackAsync(CancellationToken.None);
                return ResultFactory.Failure<TResponse>(commit.Error);
            }

            return response;
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            accessor.Pop(unitOfWork);
        }
    }
}
