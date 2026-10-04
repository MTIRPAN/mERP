using System.Reflection;
using FluentValidation;
using Merp.Application.Abstractions.Messaging;
using Merp.Infrastructure.Messaging.Behaviors;
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

        // Order matters: the first registered behavior is the outermost (see docs/PLAN.md 0.5).
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(UnitOfWorkBehavior<,>));
        return services;
    }

    /// <summary>Registers request handlers, domain event handlers and validators found in a module assembly.</summary>
    public static IServiceCollection AddRequestHandlersFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assembly);

        services.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(IRequestHandler<,>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo(typeof(IDomainEventHandler<>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo(typeof(IValidator<>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        return services;
    }

    /// <summary>Uses <typeparamref name="TUnitOfWork"/> as the transaction boundary for commands of a module.</summary>
    public static IServiceCollection AddModuleUnitOfWork<TUnitOfWork>(this IServiceCollection services, Assembly applicationAssembly)
        where TUnitOfWork : class, IUnitOfWork
    {
        ArgumentNullException.ThrowIfNull(services);
        var registry = services
            .Where(d => d.ServiceType == typeof(UnitOfWorkRegistry))
            .Select(d => d.ImplementationInstance)
            .OfType<UnitOfWorkRegistry>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Call {nameof(AddMerpMediator)} before registering module units of work.");

        registry.Register(applicationAssembly, typeof(TUnitOfWork));
        return services;
    }
}
