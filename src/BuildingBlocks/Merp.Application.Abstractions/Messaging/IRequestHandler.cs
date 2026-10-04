using Merp.SharedKernel.Results;

namespace Merp.Application.Abstractions.Messaging;

public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;

public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;

public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;
