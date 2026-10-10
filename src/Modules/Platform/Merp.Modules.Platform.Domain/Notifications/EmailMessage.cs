using Merp.Modules.Platform.Domain.Users;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;

namespace Merp.Modules.Platform.Domain.Notifications;

public readonly record struct EmailMessageId(Guid Value) : IEntityId<EmailMessageId>
{
    public static EmailMessageId From(Guid value) => new(value);
}

public enum EmailStatus
{
    Pending,
    Sent,

    /// <summary>Gave up after <see cref="EmailMessage.MaxAttempts"/> attempts.</summary>
    Failed,
}

/// <summary>
/// A rendered e-mail waiting in the queue (PLAN P8, D32). It is sent right after the transaction that queued it
/// commits; failures are retried by the <c>notifications.email</c> job with growing delays. A delivery record, not an
/// aggregate.
/// </summary>
public sealed class EmailMessage : Entity<EmailMessageId>
{
    public const int MaxAttempts = 8;
    public const int AddressMaxLength = 254;
    public const int SubjectMaxLength = 500;
    public const int ErrorMaxLength = 2000;

    private EmailMessage()
    {
    }

    public UserId? RecipientId { get; private set; }

    public string? NotificationType { get; private set; }

    public string To { get; private set; } = null!;

    public string? ToName { get; private set; }

    public string Subject { get; private set; } = null!;

    public string HtmlBody { get; private set; } = null!;

    public string TextBody { get; private set; } = null!;

    public EmailStatus Status { get; private set; }

    public int AttemptCount { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public static EmailMessage Create(
        UserId? recipientId,
        string? notificationType,
        string to,
        string? toName,
        string subject,
        string htmlBody,
        string textBody,
        DateTimeOffset now) =>
        new()
        {
            Id = EntityId.New<EmailMessageId>(),
            RecipientId = recipientId,
            NotificationType = notificationType,
            To = Text.Required(to, AddressMaxLength, nameof(to)),
            ToName = toName,
            // Subjects are a single line; a template must not be able to add headers
            Subject = Truncate(subject.ReplaceLineEndings(" ").Trim(), SubjectMaxLength),
            HtmlBody = htmlBody,
            TextBody = textBody,
            Status = EmailStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now,
        };

    public void MarkSent(DateTimeOffset at)
    {
        Status = EmailStatus.Sent;
        SentAt = at;
        AttemptCount++;
        LastError = null;
    }

    /// <summary>Schedules the next attempt (1, 2, 4... minutes, at most an hour) or gives up after the last one.</summary>
    public void RecordFailure(string error, DateTimeOffset at)
    {
        AttemptCount++;
        LastError = Truncate(error, ErrorMaxLength);
        if (AttemptCount >= MaxAttempts)
        {
            Status = EmailStatus.Failed;
            return;
        }

        NextAttemptAt = at + RetryDelay(AttemptCount);
    }

    public static TimeSpan RetryDelay(int attempt) => TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, attempt - 1)));

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}
