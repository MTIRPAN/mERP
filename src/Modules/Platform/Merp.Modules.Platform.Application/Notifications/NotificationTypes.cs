using Merp.Application.Abstractions.Notifications;
using Merp.SharedKernel.Notifications;

namespace Merp.Modules.Platform.Application.Notifications;

/// <summary>Notification types of the Platform module.</summary>
public sealed class PlatformNotificationTypes : INotificationTypeProvider
{
    /// <summary>A user sends it to themself to check their channels (Profile → Notifications).</summary>
    public const string Test = "platform.test";

    /// <summary>The religious holidays of next year are not entered yet (PLAN P9, every 1 October).</summary>
    public const string HolidaysMissing = "platform.holidays_missing";

    /// <summary>A request waits for the recipient's approval, as approver or delegate (§2.7).</summary>
    public const string ApprovalAssigned = "platform.approval_assigned";

    /// <summary>A request found no approver or no flow; administrators must act (§2.2, §2.4).</summary>
    public const string ApprovalNeedsAttention = "platform.approval_needs_attention";

    /// <summary>A task is past its due time (§2.5).</summary>
    public const string ApprovalOverdue = "platform.approval_overdue";

    public IEnumerable<NotificationTypeDefinition> GetNotificationTypes() =>
    [
        new(Test, [NotificationChannel.InApp, NotificationChannel.Email]),
        new(HolidaysMissing, [NotificationChannel.InApp, NotificationChannel.Email]),
        new(ApprovalAssigned, [NotificationChannel.InApp, NotificationChannel.Email]),
        new(ApprovalNeedsAttention, [NotificationChannel.InApp, NotificationChannel.Email]),
        new(ApprovalOverdue, [NotificationChannel.InApp, NotificationChannel.Email]),
    ];
}

/// <summary>The notification types of all modules (D32).</summary>
public sealed class NotificationTypeRegistry(IEnumerable<INotificationTypeProvider> providers)
{
    private readonly Dictionary<string, NotificationTypeDefinition> _types =
        providers.SelectMany(p => p.GetNotificationTypes()).ToDictionary(t => t.Type, StringComparer.Ordinal);

    public IReadOnlyCollection<NotificationTypeDefinition> All => _types.Values;

    public NotificationTypeDefinition? Find(string? type) => type is not null && _types.TryGetValue(type, out var definition) ? definition : null;

    /// <summary>Whether a user gets a type on a channel: their choice, otherwise the type's default. Mandatory types ignore choices.</summary>
    public static bool Receives(Domain.Users.User user, NotificationTypeDefinition type, NotificationChannel channel)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(type);
        var byDefault = type.DefaultChannels.Contains(channel);
        return type.Mandatory ? byDefault : user.Receives(type.Type, channel, byDefault);
    }
}

/// <summary>Settings of notifications; the host binds them from the <c>Notifications</c> section.</summary>
public sealed class NotificationSettings
{
    public const string SectionName = "Notifications";

    /// <summary>Address of the web application; links in e-mails start with it.</summary>
    public string WebBaseUrl { get; set; } = "http://localhost:5173";
}
