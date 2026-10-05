using System.Reflection;
using FluentValidation;
using Merp.Application.Abstractions.Messaging;
using Merp.Infrastructure.Messaging.Behaviors;
using Merp.Infrastructure.Messaging.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Merp.Infrastructure.Messaging;

public static class MediatorServiceCollectionExtensions
{
    /// <summary>Registers the mediator and the pipeline behaviors in their fixed order.</summary>
    public static IServiceCollection AddMerpMediator(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<ISender, Sender>();
        services.TryAddSingleton(new UnitOfWorkRegistry());
        services.TryAddScoped<UnitOfWorkAccessor>();
        services.TryAddScoped<AfterCommitQueue>();
        services.TryAddScoped<IAfterCommit>(sp => sp.GetRequiredService<AfterCommitQueue>());

        // Order matters: the first registered behavior is the outermost (see docs/PLAN.md 0.5).
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(UnitOfWorkBehavior<,>));
        return services;
    }

    /// <summary>
    /// Registers request handlers, domain event handlers, integration event handlers and validators found in a
    /// module assembly (optionally only in one namespace).
    /// </summary>
    public static IServiceCollection AddRequestHandlersFromAssembly(this IServiceCollection services, Assembly assembly, string? namespacePrefix = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assembly);
        var scope = new ModuleScope(assembly, namespacePrefix);

        services.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(IRequestHandler<,>)).Where(scope.Contains), publicOnly: false)
            .AsImplementedInterfaces()
            .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo(typeof(IDomainEventHandler<>)).Where(scope.Contains), publicOnly: false)
            .AsImplementedInterfaces()
            .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo(typeof(IValidator<>)).Where(scope.Contains), publicOnly: false)
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        // Integration event handlers are resolved by concrete type: the inbox stores which handler a message is for.
        foreach (var handlerType in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && scope.Contains(t)))
        {
            foreach (var eventType in handlerType.GetInterfaces()
                         .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IIntegrationEventHandler<>))
                         .Select(i => i.GetGenericArguments()[0]))
            {
                services.TryAddScoped(handlerType);
                services.AddSingleton(new IntegrationEventHandlerDescriptor(handlerType, eventType));
            }
        }

        return services;
    }

    /// <summary>Uses <typeparamref name="TUnitOfWork"/> as the transaction boundary for commands of a module.</summary>
    public static IServiceCollection AddModuleUnitOfWork<TUnitOfWork>(this IServiceCollection services, Assembly applicationAssembly, string? namespacePrefix = null)
        where TUnitOfWork : class, IUnitOfWork
    {
        ArgumentNullException.ThrowIfNull(services);
        var registry = services
            .Where(d => d.ServiceType == typeof(UnitOfWorkRegistry))
            .Select(d => d.ImplementationInstance)
            .OfType<UnitOfWorkRegistry>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Call {nameof(AddMerpMediator)} before registering module units of work.");

        registry.Register(applicationAssembly, typeof(TUnitOfWork), namespacePrefix);
        return services;
    }
}
