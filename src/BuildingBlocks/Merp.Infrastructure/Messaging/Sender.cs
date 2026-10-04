using System.Collections.Concurrent;
using Merp.Application.Abstractions.Messaging;
using Merp.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Infrastructure.Messaging;

/// <summary>In-house mediator (D19): resolves the handler and runs the pipeline behaviors around it.</summary>
internal sealed class Sender(IServiceProvider serviceProvider) : ISender
{
    private static readonly ConcurrentDictionary<Type, RequestHandlerWrapper> Wrappers = new();

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        where TResponse : Result
    {
        ArgumentNullException.ThrowIfNull(request);

        var wrapper = Wrappers.GetOrAdd(
            request.GetType(),
            static requestType => (RequestHandlerWrapper)Activator.CreateInstance(
                typeof(RequestHandlerWrapper<,>).MakeGenericType(requestType, typeof(TResponse)))!);

        return ((RequestHandlerWrapper<TResponse>)wrapper).Handle(request, serviceProvider, cancellationToken);
    }
}

internal abstract class RequestHandlerWrapper;

internal abstract class RequestHandlerWrapper<TResponse> : RequestHandlerWrapper
    where TResponse : Result
{
    public abstract Task<TResponse> Handle(IRequest<TResponse> request, IServiceProvider serviceProvider, CancellationToken cancellationToken);
}

internal sealed class RequestHandlerWrapper<TRequest, TResponse> : RequestHandlerWrapper<TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result
{
    public override Task<TResponse> Handle(IRequest<TResponse> request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var typedRequest = (TRequest)request;
        var handler = serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>()
            ?? throw new InvalidOperationException($"No handler is registered for {typeof(TRequest).FullName}.");

        RequestHandlerDelegate<TResponse> next = () => handler.Handle(typedRequest, cancellationToken);

        // Behaviors run in registration order, so the first registered behavior must be the outermost one.
        foreach (var behavior in serviceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>().Reverse())
        {
            var inner = next;
            next = () => behavior.Handle(typedRequest, inner, cancellationToken);
        }

        return next();
    }
}
