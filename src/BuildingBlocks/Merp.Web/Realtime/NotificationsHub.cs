using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Merp.Web.Realtime;

/// <summary>
/// Real-time channel to signed-in users (<c>/hubs/notifications</c>). The server only pushes (<c>notificationReceived</c>);
/// a connection belongs to its user through <see cref="TenantUserIdProvider"/> (PLAN P8).
/// </summary>
[Authorize]
public sealed class NotificationsHub : Hub
{
    public const string Path = "/hubs/notifications";
}
