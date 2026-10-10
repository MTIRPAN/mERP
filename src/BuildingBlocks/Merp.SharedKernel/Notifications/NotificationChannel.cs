namespace Merp.SharedKernel.Notifications;

/// <summary>How a notification reaches its recipient (PLAN P8).</summary>
public enum NotificationChannel
{
    /// <summary>The bell in the application, delivered at once over SignalR.</summary>
    InApp,

    Email,
}
