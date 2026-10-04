using Merp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NpgsqlTypes;

namespace Merp.Infrastructure.Messaging.Integration;

/// <summary>
/// Writes one inbox row per subscribed handler into the tenant database. Rows are unique per (message, handler), so
/// a message delivered twice is stored once (D8).
/// </summary>
public sealed class InboxWriter(IEnumerable<ModuleDatabase> modules, IEnumerable<IntegrationEventHandlerDescriptor> handlers, TimeProvider timeProvider)
{
    private readonly ModuleDatabase[] _modules = [.. modules];
    private readonly IntegrationEventHandlerDescriptor[] _handlers = [.. handlers];

    /// <summary>Modules with at least one handler for <paramref name="eventType"/>, with those handlers.</summary>
    public IReadOnlyList<(ModuleDatabase Module, IReadOnlyList<IntegrationEventHandlerDescriptor> Handlers)> Subscribers(string eventType) =>
        [.. _handlers
            .Where(h => string.Equals(h.EventTypeName, eventType, StringComparison.Ordinal))
            .GroupBy(ModuleOf)
            .Select(g => (g.Key, (IReadOnlyList<IntegrationEventHandlerDescriptor>)[.. g]))];

    /// <summary>All event types handled by a module, for RabbitMQ queue bindings.</summary>
    public IReadOnlyList<string> EventTypesHandledBy(ModuleDatabase module) =>
        [.. _handlers.Where(h => ModuleOf(h) == module).Select(h => h.EventTypeName).Distinct(StringComparer.Ordinal)];

    public async Task<int> WriteAsync(MessageEnvelope envelope, IServiceProvider tenantScope, string? moduleName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(tenantScope);
        var written = 0;
        var receivedAt = timeProvider.GetUtcNow();

        foreach (var (module, moduleHandlers) in Subscribers(envelope.Type))
        {
            if (moduleName is not null && !string.Equals(module.ModuleName, moduleName, StringComparison.Ordinal))
            {
                continue;
            }

            var db = (ModuleDbContext)tenantScope.GetRequiredService(module.DbContextType);
            foreach (var handler in moduleHandlers)
            {
                written += await db.Database.ExecuteSqlRawAsync(
                    MessagingSql.For(module.Schema).InsertInbox,
                    [
                        MessagingSql.Parameter("message_id", NpgsqlDbType.Uuid, envelope.MessageId),
                        MessagingSql.Parameter("handler", NpgsqlDbType.Varchar, handler.HandlerName),
                        MessagingSql.Parameter("type", NpgsqlDbType.Varchar, envelope.Type),
                        MessagingSql.Parameter("payload", NpgsqlDbType.Jsonb, envelope.Payload),
                        MessagingSql.Parameter("received_at", NpgsqlDbType.TimestampTz, receivedAt),
                        MessagingSql.Parameter("trace_parent", NpgsqlDbType.Varchar, envelope.TraceParent),
                    ],
                    cancellationToken);
            }
        }

        return written;
    }

    private ModuleDatabase ModuleOf(IntegrationEventHandlerDescriptor handler) =>
        ModuleScope.FindBest(_modules, m => m.Scope, handler.HandlerType)
        ?? throw new InvalidOperationException(
            $"Integration event handler {handler.HandlerName} does not belong to a module with a DbContext (AddModuleDbContext).");
}
