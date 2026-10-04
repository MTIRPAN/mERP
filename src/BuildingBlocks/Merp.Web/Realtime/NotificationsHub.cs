using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Merp.Web.Realtime;

/// <summary>Real-time channel to signed-in users (<c>/hubs/notifications</c>). Notifications arrive with PLAN P8.</summary>
[Authorize]
public sealed class NotificationsHub : Hub
{
    public const string Path = "/hubs/notifications";
}
