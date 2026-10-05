using System.Collections.Concurrent;
using Merp.Application.Abstractions.Messaging;
using Merp.SharedKernel.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Infrastructure.Messaging;

/// <summary>Invokes the <see cref="IDomainEventHandler{TEvent}"/>s of a domain event in the current scope.</summary>
public sealed class DomainEventDispatcher(IServiceProvider serviceProvider)
{
    private static readonly ConcurrentDictionary<Type, Func<IServiceProvider, IDomainEvent, CancellationToken, Task>> Invokers = new();

    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var invoker = Invokers.GetOrAdd(domainEvent.GetType(), static eventType =>
        {
            var method = typeof(DomainEventDispatcher)
                .GetMethod(nameof(InvokeHandlers), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(eventType);
            return method.CreateDelegate<Func<IServiceProvider, IDomainEvent, CancellationToken, Task>>();
        });

        await invoker(serviceProvider, domainEvent, cancellationToken);
    }

    private static async Task InvokeHandlers<TEvent>(IServiceProvider serviceProvider, IDomainEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        foreach (var handler in serviceProvider.GetServices<IDomainEventHandler<TEvent>>())
        {
            await handler.Handle((TEvent)domainEvent, cancellationToken);
        }
    }
}
