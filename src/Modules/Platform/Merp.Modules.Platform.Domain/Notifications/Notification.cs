using Merp.Modules.Platform.Domain.Users;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;

namespace Merp.Modules.Platform.Domain.Notifications;

public readonly record struct NotificationId(Guid Value) : IEntityId<NotificationId>
{
    public static NotificationId From(Guid value) => new(value);
}

/// <summary>
/// A notification in a user's bell (PLAN P8). It stores the type and arguments, not text: the title and body are
/// localized when read, in the reader's language. It is a delivery record like an outbox message, not an aggregate,
/// so one event can notify many users in one transaction (D32).
/// </summary>
public sealed class Notification : Entity<NotificationId>
{
    public const int TypeMaxLength = 100;
    public const int LinkMaxLength = 500;

    private Notification()
    {
    }

    public UserId RecipientId { get; private set; }

    /// <summary>Registered type, e.g. <c>hr.leave_approved</c>.</summary>
    public string Type { get; private set; } = null!;

    /// <summary>Values for the placeholders of the type's title and body.</summary>
    public IReadOnlyDictionary<string, string> Args { get; private set; } = new Dictionary<string, string>();

    /// <summary>Path in the web application the notification opens.</summary>
    public string? Link { get; private set; }

    public Guid? CompanyId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public static Notification Create(
        UserId recipientId,
        string type,
        IReadOnlyDictionary<string, string> args,
        string? link,
        Guid? companyId,
        DateTimeOffset now) =>
        new()
        {
            Id = EntityId.New<NotificationId>(),
            RecipientId = recipientId,
            Type = Text.Required(type, TypeMaxLength, nameof(type)),
            Args = new Dictionary<string, string>(args ?? throw new ArgumentNullException(nameof(args)), StringComparer.Ordinal),
            Link = SafeLink(link),
            CompanyId = companyId,
            CreatedAt = now,
        };

    /// <summary>Returns false when it was already read.</summary>
    public bool MarkRead(DateTimeOffset at)
    {
        if (ReadAt is not null)
        {
            return false;
        }

        ReadAt = at;
        return true;
    }

    /// <summary>Only paths inside the application: a notification never sends the user to another site.</summary>
    public static string? SafeLink(string? link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return null;
        }

        var path = link.Trim();
        return path.Length <= LinkMaxLength && path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal) && !path.Contains('\\', StringComparison.Ordinal)
            ? path
            : throw new ArgumentException($"'{link}' is not a path in the application.", nameof(link));
    }
}
