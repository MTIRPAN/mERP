using System.Security.Claims;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Notifications;
using Merp.Web.MultiTenancy;
using Microsoft.AspNetCore.SignalR;

namespace Merp.Web.Realtime;

/// <summary>
/// SignalR identifies a connection's user as <c>{tenant}:{sub}</c>: subjects come from the identity provider and
/// are only unique within a tenant.
/// </summary>
internal sealed class TenantUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var tenant = connection.User?.FindFirstValue(TenantResolutionMiddleware.TenantClaim);
        var subject = connection.User?.FindFirstValue("sub");
        return tenant is null || subject is null ? null : UserKey(tenant, subject);
    }

    public static string UserKey(string tenantCode, string subject) => $"{tenantCode.ToLowerInvariant()}:{subject}";
}

/// <summary>Sends to every open connection of a user of the current tenant (<c>/hubs/notifications</c>).</summary>
internal sealed class HubRealtimeNotifier(IHubContext<NotificationsHub> hub, ITenantContext tenant) : IRealtimeNotifier
{
    public Task SendToUserAsync(string userSubject, string method, object payload, CancellationToken cancellationToken = default) =>
        hub.Clients.User(TenantUserIdProvider.UserKey(tenant.RequiredTenant.Code, userSubject)).SendAsync(method, payload, cancellationToken);
}
