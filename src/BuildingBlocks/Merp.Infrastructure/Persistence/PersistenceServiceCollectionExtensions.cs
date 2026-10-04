using System.Reflection;
using Merp.Application.Abstractions.Messaging;
using Merp.Infrastructure.Messaging;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.Persistence.Interceptors;
using Merp.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Merp.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>Shared persistence services: interceptors, domain event dispatch, outbox publisher, migrator.</summary>
    public static IServiceCollection AddMerpPersistence(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<PersistenceOptions>();
        services.TryAddScoped<SoftDeleteInterceptor>();
        services.TryAddScoped<AuditableInterceptor>();
        services.TryAddScoped<AuditLogInterceptor>();
        services.TryAddScoped<DomainEventDispatcher>();
        services.TryAddScoped<IIntegrationEventPublisher, OutboxIntegrationEventPublisher>();
        services.TryAddSingleton<TenantDatabaseMigrator>();
        return services;
    }

    /// <summary>
    /// Registers a module DbContext bound to the current tenant's database and makes it the unit of work for the
    /// commands in <paramref name="applicationAssembly"/>.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, Assembly applicationAssembly)
        where TContext : ModuleDbContext, IModuleSchema
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddDbContext<TContext>((serviceProvider, options) =>
        {
            ConfigureModuleDbContext<TContext>(options, serviceProvider.GetRequiredService<TenantContext>().ConnectionString);

            // Order matters: soft delete turns deletes into updates, then stamps are set, then changes are audited.
            options.AddInterceptors(
                serviceProvider.GetRequiredService<SoftDeleteInterceptor>(),
                serviceProvider.GetRequiredService<AuditableInterceptor>(),
                serviceProvider.GetRequiredService<AuditLogInterceptor>());
        });

        services.AddScoped<ModuleUnitOfWork<TContext>>();
        services.AddModuleUnitOfWork<ModuleUnitOfWork<TContext>>(applicationAssembly);
        services.AddSingleton(new ModuleDatabase(typeof(TContext), TContext.SchemaName));
        return services;
    }

    /// <summary>Provider settings shared by runtime registration and design-time factories.</summary>
    public static DbContextOptionsBuilder ConfigureModuleDbContext<TContext>(DbContextOptionsBuilder options, string connectionString)
        where TContext : ModuleDbContext, IModuleSchema
    {
        ArgumentNullException.ThrowIfNull(options);
        return options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(ModuleDbContext.MigrationsHistoryTable, TContext.SchemaName))
            .UseSnakeCaseNamingConvention();
    }
}
