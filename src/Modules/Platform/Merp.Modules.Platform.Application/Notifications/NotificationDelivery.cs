using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Notifications;
using Merp.Application.Abstractions.Settings;
using Merp.Modules.Platform.Application.Settings;
using Merp.Modules.Platform.Contracts.Notifications;
using Merp.Modules.Platform.Domain.Notifications;
using Merp.Modules.Platform.Domain.Users;
using Merp.SharedKernel.Notifications;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Notifications;

/// <summary>Payload of the <c>notificationReceived</c> SignalR message; the client reloads its list and count.</summary>
public sealed record NotificationPushed(Guid Id, string Type);

/// <summary>
/// Delivers <see cref="NotificationRequested"/> (PLAN P8, D32): for each active recipient, a notification in the bell
/// and an e-mail in the queue, each as their preferences allow. E-mails go out only where the tenant or the company
/// keeps them on (D35). After the commit the open browser tabs are told at once and the e-mails are sent.
/// </summary>
internal sealed class NotificationRequestedHandler(
    IPlatformDbContext db,
    NotificationTypeRegistry types,
    EmailComposer composer,
    IRealtimeNotifier realtime,
    IAfterCommit afterCommit,
    ISender sender,
    ISettings settings,
    TimeProvider timeProvider) : IIntegrationEventHandler<NotificationRequested>
{
    public async Task Handle(NotificationRequested integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        // An unknown type is a programming error of the sender: let the inbox retry and dead-letter it visibly
        var type = types.Find(integrationEvent.Type)
            ?? throw new InvalidOperationException($"Notification type {integrationEvent.Type} is not registered.");
        var now = timeProvider.GetUtcNow();
        var link = Notification.SafeLink(integrationEvent.Link);

        var ids = integrationEvent.RecipientUserIds.Distinct().Select(UserId.From).ToList();
        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id) && u.IsActive).ToListAsync(cancellationToken);
        var emailEnabled = await settings.GetAsync<bool>(PlatformSettingDefinitions.EmailNotificationsEnabled, integrationEvent.CompanyId, cancellationToken);

        var pushes = new List<(string Subject, NotificationPushed Payload)>();
        var emails = new List<Guid>();
        foreach (var user in users)
        {
            if (NotificationTypeRegistry.Receives(user, type, NotificationChannel.InApp))
            {
                var notification = Notification.Create(user.Id, type.Type, integrationEvent.Args, link, integrationEvent.CompanyId, now);
                db.Notifications.Add(notification);
                pushes.Add((user.Subject, new NotificationPushed(notification.Id.Value, type.Type)));
            }

            if (emailEnabled && user.Email is not null && NotificationTypeRegistry.Receives(user, type, NotificationChannel.Email))
            {
                var email = await composer.ComposeAsync(type, user, integrationEvent.Args, link, integrationEvent.CompanyId, now, cancellationToken);
                email.Attach(EmailQueue.Attachments(integrationEvent.Attachments));
                db.EmailMessages.Add(email);
                emails.Add(email.Id.Value);
            }
        }

        afterCommit.Enqueue(async ct =>
        {
            foreach (var (subject, payload) in pushes)
            {
                await realtime.SendToUserAsync(subject, "notificationReceived", payload, ct);
            }
        });

        if (emails.Count > 0)
        {
            afterCommit.Enqueue(ct => sender.Send(new SendQueuedEmailsCommand(emails), ct));
        }
    }
}

/// <summary>
/// Delivers <see cref="ExternalEmailRequested"/> (D47): an e-mail to an address outside the platform, in the queue and
/// sent after the commit, where the tenant or the company keeps e-mails on.
/// </summary>
internal sealed class ExternalEmailRequestedHandler(
    IPlatformDbContext db,
    NotificationTypeRegistry types,
    EmailComposer composer,
    IAfterCommit afterCommit,
    ISender sender,
    ISettings settings,
    TimeProvider timeProvider) : IIntegrationEventHandler<ExternalEmailRequested>
{
    public async Task Handle(ExternalEmailRequested integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var type = types.Find(integrationEvent.Type) is { External: true } found
            ? found
            : throw new InvalidOperationException($"External notification type {integrationEvent.Type} is not registered.");
        if (!await settings.GetAsync<bool>(PlatformSettingDefinitions.EmailNotificationsEnabled, integrationEvent.CompanyId, cancellationToken))
        {
            return;
        }

        var email = await composer.ComposeExternalAsync(
            type,
            integrationEvent.To,
            integrationEvent.ToName,
            integrationEvent.Culture,
            integrationEvent.Args,
            Notification.SafeLink(integrationEvent.Link),
            integrationEvent.CompanyId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        email.Attach(EmailQueue.Attachments(integrationEvent.Attachments));
        db.EmailMessages.Add(email);
        afterCommit.Enqueue(ct => sender.Send(new SendQueuedEmailsCommand([email.Id.Value]), ct));
    }
}

/// <summary>Sends queued e-mails now; those that fail stay in the queue for the retry job.</summary>
public sealed record SendQueuedEmailsCommand(IReadOnlyList<Guid> Ids) : ICommand;

/// <summary>Sends the e-mails whose next attempt is due (job <c>notifications.email</c>, every minute).</summary>
public sealed record SendDueEmailsCommand(int BatchSize = 50) : ICommand;

internal sealed class SendQueuedEmailsCommandHandler(IPlatformDbContext db, IEmailSender emailSender, TimeProvider timeProvider)
    : ICommandHandler<SendQueuedEmailsCommand>
{
    public async Task<Result> Handle(SendQueuedEmailsCommand request, CancellationToken cancellationToken)
    {
        var ids = request.Ids.Select(EmailMessageId.From).ToList();
        var messages = await db.EmailMessages.Where(m => ids.Contains(m.Id) && m.Status == EmailStatus.Pending).ToListAsync(cancellationToken);
        await EmailQueue.SendAsync(messages, emailSender, timeProvider, cancellationToken);
        return Result.Success();
    }
}

internal sealed class SendDueEmailsCommandHandler(IPlatformDbContext db, IEmailSender emailSender, TimeProvider timeProvider)
    : ICommandHandler<SendDueEmailsCommand>
{
    public async Task<Result> Handle(SendDueEmailsCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var messages = await db.EmailMessages
            .Where(m => m.Status == EmailStatus.Pending && m.NextAttemptAt <= now)
            .OrderBy(m => m.NextAttemptAt)
            .Take(request.BatchSize)
            .ToListAsync(cancellationToken);
        await EmailQueue.SendAsync(messages, emailSender, timeProvider, cancellationToken);
        return Result.Success();
    }
}

internal static class EmailQueue
{
    public static List<EmailMessageAttachment> Attachments(IReadOnlyList<EmailFile>? files) =>
        [.. (files ?? []).Select(f => new EmailMessageAttachment(f.FileName, f.ContentType, f.Content))];

    /// <summary>Each message on its own: one refused address does not hold back the others.</summary>
    public static async Task SendAsync(IEnumerable<EmailMessage> messages, IEmailSender emailSender, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            try
            {
                await emailSender.SendAsync(
                    new EmailContent(
                        message.To,
                        message.ToName,
                        message.Subject,
                        message.HtmlBody,
                        message.TextBody,
                        [.. message.Attachments.Select(a => new EmailAttachment(a.FileName, a.ContentType, a.Content))]),
                    cancellationToken);
                message.MarkSent(timeProvider.GetUtcNow());
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                message.RecordFailure(exception.Message, timeProvider.GetUtcNow());
            }
        }
    }
}
