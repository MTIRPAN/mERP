using Merp.SharedKernel.Results;

namespace Merp.Application.Abstractions.Messaging;

/// <summary>
/// Request handled by exactly one handler through <see cref="ISender"/>. Use <see cref="ICommand"/>,
/// <see cref="ICommand{TResponse}"/> or <see cref="IQuery{TResponse}"/> instead of implementing this directly.
/// </summary>
public interface IRequest<TResponse>
    where TResponse : Result;

/// <summary>Marker for requests that change state. Commands run inside a unit of work (transaction + outbox).</summary>
public interface IBaseCommand;

/// <summary>State-changing request without a return value.</summary>
public interface ICommand : IRequest<Result>, IBaseCommand;

/// <summary>State-changing request that returns a value, typically the id of the created aggregate.</summary>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>, IBaseCommand;

/// <summary>Read-only request. Queries never open a unit of work.</summary>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;
