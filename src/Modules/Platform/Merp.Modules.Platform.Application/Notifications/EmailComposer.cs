using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Notifications;
using Merp.Modules.Platform.Domain.Companies;
using Merp.Modules.Platform.Domain.Notifications;
using Merp.Modules.Platform.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Notifications;

/// <summary>
/// Turns a notification into an e-mail in the recipient's language with the most specific template: the type's own
/// before the general one, the company's before the tenant's, the user's culture before the default (D32).
/// </summary>
public sealed class EmailComposer(
    IPlatformDbContext db,
    ITemplateRenderer renderer,
    ITextLocalizer localizer,
    ITenantContext tenant,
    NotificationSettings settings)
{
    /// <summary>The built-in layout used when the tenant has no template.</summary>
    public const string DefaultSubject = "{{ title }}";

    public const string DefaultBody = """
        <!doctype html>
        <html>
        <body style="margin:0;padding:24px;background:#f5f5f5;font-family:'Segoe UI',Arial,sans-serif;color:#1f1f1f">
        <table role="presentation" width="100%" style="max-width:560px;margin:0 auto;background:#ffffff;border-radius:8px">
        <tr><td style="padding:24px">
        {% if company %}<p style="font-size:12px;color:#8c8c8c;margin:0 0 16px">{{ company.name }}</p>{% endif %}
        <h1 style="font-size:18px;margin:0 0 12px">{{ title }}</h1>
        {% if body %}<p style="font-size:14px;line-height:1.5;margin:0 0 16px">{{ body }}</p>{% endif %}
        {% if link %}<p style="margin:0 0 16px"><a href="{{ link }}" style="display:inline-block;padding:8px 16px;background:#1677ff;color:#ffffff;border-radius:6px;text-decoration:none">{{ texts.open }}</a></p>{% endif %}
        <p style="font-size:12px;color:#8c8c8c;margin:16px 0 0">{{ texts.footer }}</p>
        </td></tr>
        </table>
        </body>
        </html>
        """;

    public async Task<EmailMessage> ComposeAsync(
        NotificationTypeDefinition type,
        User user,
        IReadOnlyDictionary<string, string> args,
        string? link,
        Guid? companyId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var company = companyId is { } id
            ? await db.Companies.AsNoTracking().Where(c => c.Id == CompanyId.From(id)).Select(c => new { c.ShortName, c.Culture }).SingleOrDefaultAsync(cancellationToken)
            : null;
        var defaultCulture = tenant.RequiredTenant.DefaultCulture;
        var culture = user.Culture ?? company?.Culture ?? defaultCulture;

        var model = Model(type, user.DisplayName, user.Email, args, link, company?.ShortName, culture);
        return await RenderAsync(type, model, user.Id, user.Email!, user.DisplayName, companyId, culture, defaultCulture, now, cancellationToken);
    }

    /// <summary>An e-mail to an address outside the platform (D47), in the given culture or the company's or tenant's.</summary>
    public async Task<EmailMessage> ComposeExternalAsync(
        NotificationTypeDefinition type,
        string to,
        string? toName,
        string? culture,
        IReadOnlyDictionary<string, string> args,
        Guid? companyId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var company = companyId is { } id
            ? await db.Companies.AsNoTracking().Where(c => c.Id == CompanyId.From(id)).Select(c => new { c.ShortName, c.Culture }).SingleOrDefaultAsync(cancellationToken)
            : null;
        var defaultCulture = tenant.RequiredTenant.DefaultCulture;
        var language = culture ?? company?.Culture ?? defaultCulture;
        var model = Model(type, toName, to, args, null, company?.ShortName, language);
        return await RenderAsync(type, model, null, to, toName, companyId, language, defaultCulture, now, cancellationToken);
    }

    private async Task<EmailMessage> RenderAsync(
        NotificationTypeDefinition type,
        Dictionary<string, object?> model,
        UserId? recipientId,
        string to,
        string? toName,
        Guid? companyId,
        string culture,
        string defaultCulture,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var template = await FindTemplateAsync(type.Type, companyId, culture, defaultCulture, cancellationToken);
        var subject = renderer.Render(template?.Subject ?? DefaultSubject, model, html: false);
        var body = renderer.Render(template?.Body ?? DefaultBody, model, html: true);
        if (subject.IsFailure || body.IsFailure)
        {
            // Templates are checked when saved; should one still fail, the message goes out in the built-in layout
            subject = renderer.Render(DefaultSubject, model, html: false);
            body = renderer.Render(DefaultBody, model, html: true);
        }

        var title = (string)model["title"]!;
        var text = string.Join("\n\n", new[] { title, (string?)model["body"], (string?)model["link"] }.Where(p => !string.IsNullOrEmpty(p)));
        return EmailMessage.Create(recipientId, type.Type, to, toName, subject.Value, body.Value, text, now);
    }

    /// <summary>Title and body of a notification in a culture; the body is null when the type has none.</summary>
    public (string Title, string? Body) Texts(NotificationTypeDefinition type, IReadOnlyDictionary<string, string> args, string? culture)
    {
        var values = args.ToDictionary(p => p.Key, p => (object?)p.Value, StringComparer.Ordinal);
        var title = localizer.Get(type.TitleKey, values, culture);
        var body = localizer.Get(type.BodyKey, values, culture);
        return (title, string.Equals(body, type.BodyKey, StringComparison.Ordinal) ? null : body);
    }

    private Dictionary<string, object?> Model(
        NotificationTypeDefinition type,
        string? recipientName,
        string? recipientEmail,
        IReadOnlyDictionary<string, string> args,
        string? link,
        string? companyName,
        string culture)
    {
        var (title, body) = Texts(type, args, culture);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["title"] = title,
            ["body"] = body,
            ["link"] = link is null ? null : settings.WebBaseUrl.TrimEnd('/') + link,
            ["recipient"] = new Dictionary<string, object?> { ["name"] = recipientName, ["email"] = recipientEmail },
            ["company"] = companyName is null ? null : new Dictionary<string, object?> { ["name"] = companyName },
            ["args"] = args.ToDictionary(p => p.Key, p => (object?)p.Value, StringComparer.Ordinal),
            ["texts"] = new Dictionary<string, object?>
            {
                ["open"] = localizer.Get("platform.notifications.email.open", culture: culture),
                ["footer"] = localizer.Get("platform.notifications.email.footer", culture: culture),
            },
        };
    }

    private async Task<NotificationTemplate?> FindTemplateAsync(
        string type,
        Guid? companyId,
        string culture,
        string defaultCulture,
        CancellationToken cancellationToken)
    {
        var candidates = await db.NotificationTemplates.AsNoTracking()
            .Where(t => (t.NotificationType == type || t.NotificationType == null)
                && (t.CompanyId == companyId || t.CompanyId == null)
                && (t.Culture == culture || t.Culture == defaultCulture))
            .ToListAsync(cancellationToken);

        // Culture first (a reader must understand it), then the type's own layout, then the company's
        return candidates
            .OrderByDescending(t => string.Equals(t.Culture, culture, StringComparison.Ordinal))
            .ThenByDescending(t => t.NotificationType is not null)
            .ThenByDescending(t => t.CompanyId is not null)
            .FirstOrDefault();
    }
}
