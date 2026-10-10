using System.Globalization;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;

namespace Merp.Modules.Platform.Domain.Notifications;

public readonly record struct NotificationTemplateId(Guid Value) : IEntityId<NotificationTemplateId>
{
    public static NotificationTemplateId From(Guid value) => new(value);
}

/// <summary>
/// An e-mail template (Liquid, PLAN P8) for one notification type or for all of them, for one company or all, in one
/// culture. The recipient's culture comes first, then the type's own template, then the company's (D32). Without any,
/// the built-in layout is used.
/// </summary>
[Audited]
public sealed class NotificationTemplate : AuditableAggregateRoot<NotificationTemplateId>
{
    public const int SubjectMaxLength = 500;
    public const int BodyMaxLength = 100_000;

    private NotificationTemplate()
    {
    }

    /// <summary>The notification type, or null for every type without its own template.</summary>
    public string? NotificationType { get; private set; }

    /// <summary>The company, or null for every company.</summary>
    public Guid? CompanyId { get; private set; }

    public string Culture { get; private set; } = null!;

    /// <summary>Liquid, rendered as plain text.</summary>
    public string Subject { get; private set; } = null!;

    /// <summary>Liquid, rendered as HTML.</summary>
    public string Body { get; private set; } = null!;

    public static NotificationTemplate Create(string? notificationType, Guid? companyId, string culture, string subject, string body)
    {
        var template = new NotificationTemplate
        {
            Id = EntityId.New<NotificationTemplateId>(),
            NotificationType = string.IsNullOrWhiteSpace(notificationType) ? null : Text.Required(notificationType, Notification.TypeMaxLength, nameof(notificationType)),
            CompanyId = companyId,
            Culture = CultureInfo.GetCultureInfo(culture, predefinedOnly: true).Name,
        };
        template.Update(subject, body);
        return template;
    }

    public void Update(string subject, string body)
    {
        Subject = Text.Required(subject, SubjectMaxLength, nameof(subject));
        Body = Text.Required(body, BodyMaxLength, nameof(body));
    }
}
