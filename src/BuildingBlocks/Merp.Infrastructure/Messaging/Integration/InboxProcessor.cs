using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Merp.Application.Abstractions.Messaging;
using Merp.Infrastructure.MultiTenancy;
using Merp.Infrastructure.Persistence;
using Merp.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NpgsqlTypes;

namespace Merp.Infrastructure.Messaging.Integration;

/// <summary>
/// Runs integration event handlers for pending inbox rows of one module and tenant. Each message is handled in the
/// module's unit of work together with marking the row processed, so a handler's changes and its inbox entry
/// commit or roll back together (D8).
/// </summary>
public sealed partial class InboxProcessor(
    TenantScopeFactory scopeFactory,
    IEnumerable<IntegrationEventHandlerDescriptor> handlers,
    IOptions<MessagingOptions> options,
    TimeProvider timeProvider,
    ILogger<InboxProcessor> logger)
{
    private static readonly ActivitySource ActivitySource = new("Merp.Messaging");
    private static readonly ConcurrentDictionary<Type, Func<object, IntegrationEvent, CancellationToken, Task>> Invokers = new();

    private readonly Dictionary<string, IntegrationEventHandlerDescriptor> _handlers =
        handlers.GroupBy(h => h.HandlerName, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    public async Task<int> ProcessAsync(TenantRecord tenant, ModuleDatabase module, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(module);
        var processed = 0;
        while (processed < options.Value.BatchSize && await ProcessNextAsync(tenant, module, cancellationToken))
        {
            processed++;
        }

        return processed;
    }

    /// <summary>Handles one pending message. Returns false when nothing was pending.</summary>
    private async Task<bool> ProcessNextAsync(TenantRecord tenant, ModuleDatabase module, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateScope(tenant);
        var services = scope.ServiceProvider;
        var db = (ModuleDbContext)services.GetRequiredService(module.DbContextType);
        var unitOfWork = (IUnitOfWork)services.GetRequiredService(module.UnitOfWorkType);
        var accessor = services.GetRequiredService<UnitOfWorkAccessor>();

        await unitOfWork.BeginAsync(cancellationToken);
        accessor.Push(unitOfWork);
        Persistence.Inbox.InboxMessage? message = null;
        try
        {
            var now = timeProvider.GetUtcNow();
            message = await db.InboxMessages
                .FromSqlRaw(MessagingSql.For(module.Schema).SelectNextInbox, now)
                .SingleOrDefaultAsync(cancellationToken);

            if (message is null)
            {
                await unitOfWork.RollbackAsync(CancellationToken.None);
                return false;
            }

            using var activity = ActivitySource.StartActivity($"inbox {message.Type}", ActivityKind.Consumer, message.TraceParent);
            await InvokeHandlerAsync(services, message, cancellationToken);
            message.ProcessedAt = timeProvider.GetUtcNow();

            var commit = await unitOfWork.CommitAsync(cancellationToken);
            if (commit.IsFailure)
            {
                throw new InvalidOperationException($"Committing the handler's changes failed: {commit.Error.Code}");
            }

            MessagingTelemetry.InboxProcessed.Add(1);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException && message is not null)
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            await RecordFailureAsync(db, module, message, exception, tenant.Code);
            return true;
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

    private async Task InvokeHandlerAsync(IServiceProvider services, Persistence.Inbox.InboxMessage message, CancellationToken cancellationToken)
    {
        if (!_handlers.TryGetValue(message.Handler, out var descriptor))
        {
            throw new InvalidOperationException($"No integration event handler named {message.Handler} is registered.");
        }

        var integrationEvent = (IntegrationEvent?)JsonSerializer.Deserialize(message.Payload, descriptor.EventType, OutboxIntegrationEventPublisher.SerializerOptions)
            ?? throw new InvalidOperationException($"Message {message.MessageId} has an empty payload.");

        var handler = services.GetRequiredService(descriptor.HandlerType);
        var invoker = Invokers.GetOrAdd(descriptor.EventType, static eventType =>
            typeof(InboxProcessor).GetMethod(nameof(Invoke), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(eventType)
                .CreateDelegate<Func<object, IntegrationEvent, CancellationToken, Task>>());

        await invoker(handler, integrationEvent, cancellationToken);
    }

    private static Task Invoke<TEvent>(object handler, IntegrationEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IntegrationEvent =>
        ((IIntegrationEventHandler<TEvent>)handler).Handle((TEvent)integrationEvent, cancellationToken);

    private async Task RecordFailureAsync(ModuleDbContext db, ModuleDatabase module, Persistence.Inbox.InboxMessage message, Exception exception, string tenantCode)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var attempt = message.AttemptCount + 1;
        var deadLetter = attempt >= settings.MaxAttempts;
        MessagingTelemetry.InboxFailed.Add(1);

        await db.Database.ExecuteSqlRawAsync(
            MessagingSql.For(module.Schema).RecordInboxFailure,
            [
                MessagingSql.Parameter("attempt_count", NpgsqlDbType.Integer, attempt),
                MessagingSql.Parameter("last_error", NpgsqlDbType.Text, OutboxDispatcher.Truncate(exception.ToString())),
                MessagingSql.Parameter("next_attempt_at", NpgsqlDbType.TimestampTz, deadLetter ? null : now + settings.RetryDelay(attempt)),
                MessagingSql.Parameter("dead_lettered_at", NpgsqlDbType.TimestampTz, deadLetter ? now : null),
                MessagingSql.Parameter("message_id", NpgsqlDbType.Uuid, message.MessageId),
                MessagingSql.Parameter("handler", NpgsqlDbType.Varchar, message.Handler),
            ],
            CancellationToken.None);

        if (deadLetter)
        {
            MessagingTelemetry.InboxDeadLettered.Add(1);
            LogDeadLettered(logger, exception, message.MessageId, message.Handler, tenantCode);
        }
        else
        {
            LogRetry(logger, exception, message.MessageId, message.Handler, tenantCode, attempt);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handler {Handler} failed for message {MessageId} of tenant {TenantCode}, attempt {Attempt}")]
    private static partial void LogRetry(ILogger logger, Exception exception, Guid messageId, string handler, string tenantCode, int attempt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Message {MessageId} for handler {Handler} of tenant {TenantCode} was dead-lettered")]
    private static partial void LogDeadLettered(ILogger logger, Exception exception, Guid messageId, string handler, string tenantCode);
}
