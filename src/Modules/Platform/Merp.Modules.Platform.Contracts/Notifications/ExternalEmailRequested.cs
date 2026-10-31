using Merp.Application.Abstractions.Messaging;

namespace Merp.Modules.Platform.Contracts.Notifications;

/// <summary>
/// Asks Platform to e-mail someone who is not a platform user, e.g. a candidate (D47). The type is an external
/// notification type; its title and body (or the tenant's template) are written in <paramref name="Culture"/>, and
/// IntegrationEvent.CompanyId picks the company's template. Delivery goes through the e-mail queue with its retries.
/// </summary>
/// <param name="Type">A registered external notification type, e.g. <c>recruitment.application_received</c>.</param>
/// <param name="To">The address.</param>
/// <param name="ToName">The recipient's name, shown in the template as <c>recipient.name</c>.</param>
/// <param name="Culture">Language of the message; the company's or the tenant's when null.</param>
/// <param name="Args">Values for the placeholders of the type's title and body.</param>
public sealed record ExternalEmailRequested(
    string Type,
    string To,
    string? ToName,
    string? Culture,
    IReadOnlyDictionary<string, string> Args) : IntegrationEvent
{
    /// <summary>Files sent with the e-mail, e.g. a calendar invitation (D52).</summary>
    public IReadOnlyList<EmailFile> Attachments { get; init; } = [];

    /// <summary>A path of the web application the e-mail links to, e.g. the answer page of a job offer (D53).</summary>
    public string? Link { get; init; }

    /// <summary>
    /// A subject the sender wrote for this e-mail, e.g. a personalized candidate e-mail (D58). It replaces the type's
    /// title, and its <c>{name}</c> placeholders are filled from <see cref="Args"/>; null keeps the type's title.
    /// </summary>
    public string? Subject { get; init; }

    /// <summary>A plain text body the sender wrote, replacing the type's body like <see cref="Subject"/>.</summary>
    public string? Body { get; init; }
}
