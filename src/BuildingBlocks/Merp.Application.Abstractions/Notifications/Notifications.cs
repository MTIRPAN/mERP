using Merp.SharedKernel.Notifications;
using Merp.SharedKernel.Results;

namespace Merp.Application.Abstractions.Notifications;

/// <summary>
/// A kind of notification a module sends, e.g. <c>hr.leave_approved</c>. The first segment is the module. Texts are
/// localization keys of that module: <c>{module}.notifications.{name}.title</c>, <c>.body</c> and <c>.label</c> (the
/// name in the preference list). Title and body take the notification's arguments as named placeholders.
/// </summary>
/// <param name="Type">Stable code, <c>{module}.{name}</c> in lower case.</param>
/// <param name="DefaultChannels">Channels a user gets until they change their preferences.</param>
/// <param name="Mandatory">Users cannot switch it off (e.g. security messages).</param>
public sealed record NotificationTypeDefinition(string Type, IReadOnlyList<NotificationChannel> DefaultChannels, bool Mandatory = false)
{
    public string Module => Type.Split('.')[0];

    private string Name => Type[(Module.Length + 1)..];

    public string TitleKey => $"{Module}.notifications.{Name}.title";

    public string BodyKey => $"{Module}.notifications.{Name}.body";

    public string LabelKey => $"{Module}.notifications.{Name}.label";
}

/// <summary>Modules declare their notification types, like their permissions.</summary>
public interface INotificationTypeProvider
{
    IEnumerable<NotificationTypeDefinition> GetNotificationTypes();
}

/// <summary>Pushes a message to a signed-in user's open browser tabs (<c>/hubs/notifications</c>).</summary>
public interface IRealtimeNotifier
{
    /// <param name="userSubject">The user's identity provider id (<c>sub</c>) in the current tenant.</param>
    /// <param name="method">Client method, e.g. <c>notificationReceived</c>.</param>
    /// <param name="payload">Serialized as JSON.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    Task SendToUserAsync(string userSubject, string method, object payload, CancellationToken cancellationToken = default);
}

/// <summary>An e-mail ready to send.</summary>
public sealed record EmailContent(string To, string? ToName, string Subject, string HtmlBody, string TextBody);

/// <summary>Sends e-mail over SMTP (<c>Email</c> section). Throws when the server refuses or cannot be reached.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailContent email, CancellationToken cancellationToken = default);
}

/// <summary>
/// Renders Liquid templates (Fluid, D14) with only the given model's members; HTML is encoded unless a template asks
/// otherwise. Used for e-mails (P8) and documents (P12).
/// </summary>
public interface ITemplateRenderer
{
    /// <summary>Checks a template; the error names the line and the problem.</summary>
    Result Validate(string template);

    /// <param name="template">Liquid source.</param>
    /// <param name="model">Values by name; nested dictionaries and lists are allowed.</param>
    /// <param name="html">Encode output for HTML (bodies) or not (subjects, plain text).</param>
    Result<string> Render(string template, IReadOnlyDictionary<string, object?> model, bool html);
}

public static class TemplateErrors
{
    /// <summary>Args: <c>line</c>, <c>message</c>.</summary>
    public const string InvalidCode = "common.templates.errors.invalid";
}
