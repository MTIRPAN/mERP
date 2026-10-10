using Merp.Application.Abstractions.Messaging;

namespace Merp.Modules.Platform.Contracts.Notifications;

/// <summary>
/// Asks Platform to notify users (PLAN P8, D32). Any module publishes it through its outbox; Platform applies each
/// recipient's preferences and delivers in the application and by e-mail. IntegrationEvent.CompanyId, when set, picks
/// the company's e-mail template.
/// </summary>
/// <param name="Type">A registered notification type, e.g. <c>hr.leave_approved</c>.</param>
/// <param name="RecipientUserIds">Platform users; inactive or unknown users are skipped.</param>
/// <param name="Args">Values for the placeholders of the type's title and body, e.g. <c>number</c>.</param>
/// <param name="Link">Path in the web application, e.g. <c>/hr/leave/requests/42</c>.</param>
public sealed record NotificationRequested(
    string Type,
    IReadOnlyList<Guid> RecipientUserIds,
    IReadOnlyDictionary<string, string> Args,
    string? Link) : IntegrationEvent;
