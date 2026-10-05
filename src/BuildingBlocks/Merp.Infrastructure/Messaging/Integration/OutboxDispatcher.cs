using Merp.Application.Abstractions.Context;
using Merp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Merp.Infrastructure.Messaging.Integration;

/// <summary>
/// Hands pending outbox messages of one module and tenant to the transport. Rows are locked with
/// <c>FOR UPDATE SKIP LOCKED</c> so several instances can dispatch in parallel; failures are retried with
/// exponential backoff and dead-lettered after <see cref="MessagingOptions.MaxAttempts"/> (D8).
/// </summary>
public sealed partial class OutboxDispatcher(
    IMessageTransport transport,
    IOptions<MessagingOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcher> logger)
{
    /// <summary>Returns the number of messages handed over successfully.</summary>
    public async Task<int> DispatchAsync(IServiceProvider tenantScope, ModuleDatabase module, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        ArgumentNullException.ThrowIfNull(module);
        var settings = options.Value;
        var tenant = tenantScope.GetRequiredService<ITenantContext>().RequiredTenant;
        var db = (ModuleDbContext)tenantScope.GetRequiredService(module.DbContextType);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var messages = await db.OutboxMessages
            .FromSqlRaw(MessagingSql.For(module.Schema).SelectPendingOutbox, now, settings.BatchSize)
            .ToListAsync(cancellationToken);

        var dispatched = 0;
        foreach (var message in messages)
        {
            try
            {
                await transport.SendAsync(
                    new MessageEnvelope(message.Id, message.Type, message.Payload, tenant.Id, message.OccurredAt, message.TraceParent),
                    tenantScope,
                    cancellationToken);

                message.ProcessedAt = timeProvider.GetUtcNow();
                dispatched++;
                MessagingTelemetry.OutboxDispatched.Add(1);
                MessagingTelemetry.OutboxLatency.Record((message.ProcessedAt.Value - message.OccurredAt).TotalMilliseconds);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                message.AttemptCount++;
                message.LastError = Truncate(exception.ToString());
                MessagingTelemetry.OutboxFailed.Add(1);
                if (message.AttemptCount >= settings.MaxAttempts)
                {
                    message.DeadLetteredAt = now;
                    MessagingTelemetry.OutboxDeadLettered.Add(1);
                    LogDeadLettered(logger, exception, message.Id, message.Type, tenant.Code);
                }
                else
                {
                    message.NextAttemptAt = now + settings.RetryDelay(message.AttemptCount);
                    LogRetry(logger, exception, message.Id, message.Type, tenant.Code, message.AttemptCount);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return dispatched;
    }

    internal static string Truncate(string value) => value.Length <= 4000 ? value : value[..4000];

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dispatching outbox message {MessageId} ({MessageType}) of tenant {TenantCode} failed, attempt {Attempt}")]
    private static partial void LogRetry(ILogger logger, Exception exception, Guid messageId, string messageType, string tenantCode, int attempt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox message {MessageId} ({MessageType}) of tenant {TenantCode} was dead-lettered")]
    private static partial void LogDeadLettered(ILogger logger, Exception exception, Guid messageId, string messageType, string tenantCode);
}
