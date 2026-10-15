using System.Globalization;
using System.Net;
using Merp.Application.Abstractions.Documents;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Notifications;
using Merp.Application.Persistence.Files;
using Merp.Modules.Platform.Application.Files;
using Merp.Modules.Platform.Domain.Companies;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Documents;

/// <summary>Content given in the editor instead of the saved template (previews).</summary>
public sealed record DocumentContent(string Body, string? Css);

/// <param name="Html">A self-contained page: styles inline, logo as a data: URI.</param>
/// <param name="TemplateId">The tenant's template used, or null for the built-in one.</param>
/// <param name="Revision">Revision of that template; 0 for the built-in one.</param>
public sealed record ComposedDocument(string Html, Guid? TemplateId, int Revision);

/// <summary>
/// Builds a document's HTML page (D36): the most specific template (the company's before the one for all companies,
/// only in the asked culture, otherwise the built-in one), the model with <c>company</c>, <c>document</c> and
/// <c>texts</c>, the <c>logo</c> and <c>letterhead</c> partials and the base style sheet.
/// </summary>
public sealed class DocumentComposer(IPlatformDbContext db, ITemplateRenderer renderer, ITextLocalizer localizer, IFileStore files, TimeProvider time)
{
    /// <summary>Field of template errors, so the editor can mark the line.</summary>
    public const string BodyField = "body";

    private const string CommonTexts = "platform.documents.texts.";

    private static readonly IReadOnlyDictionary<string, string> Partials = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["logo"] = """{% if company.logo %}<img class="logo" src="{{ company.logo }}" alt="">{% endif %}""",
        ["letterhead"] = """
            <header class="letterhead">
              {% render 'logo' %}
              <div class="company">
                <div class="title">{{ company.title }}</div>
                {% for line in company.address_lines %}<div>{{ line }}</div>{% endfor %}
                <div class="contact">{% if company.phone %}{{ texts.phone }}: {{ company.phone }}{% endif %}{% if company.phone and company.email %} · {% endif %}{% if company.email %}{{ company.email }}{% endif %}</div>
                <div class="tax">{% if company.tax_office %}{{ texts.tax_office }}: {{ company.tax_office }} · {% endif %}{{ texts.tax_number }}: {{ company.tax_number }}{% if company.mersis_number %} · {{ texts.mersis_number }}: {{ company.mersis_number }}{% endif %}</div>
              </div>
            </header>
            """,
    };

    /// <summary>A4 with print margins and a font with Turkish letters; the template's own style sheet comes after it.</summary>
    private const string BaseCss = """
        @page { size: A4; margin: 18mm 18mm 20mm 18mm; }
        * { box-sizing: border-box; }
        body { margin: 0; font-family: 'Noto Sans', 'DejaVu Sans', 'Liberation Sans', Arial, sans-serif; font-size: 10.5pt; color: #1f1f1f; }
        .letterhead { display: flex; gap: 16px; align-items: center; padding-bottom: 10px; margin-bottom: 24px; border-bottom: 1px solid #d9d9d9; }
        .letterhead .logo { max-height: 60px; max-width: 180px; }
        .letterhead .title { font-size: 13pt; font-weight: 600; }
        .letterhead .contact, .letterhead .tax { font-size: 8.5pt; color: #595959; }
        """;

    /// <summary>The company of a template for all companies when previewing: made up, like the sample model.</summary>
    private static readonly IReadOnlyDictionary<string, object?> SampleCompany = new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["code"] = "ORNEK",
        ["title"] = "Örnek Holding A.Ş.",
        ["short_name"] = "Örnek Holding",
        ["tax_office"] = "Büyük Mükellefler",
        ["tax_number"] = "1234567890",
        ["mersis_number"] = null,
        ["address_lines"] = new List<object?> { "Büyükdere Cad. No:1", "34394 Şişli / İstanbul" },
        ["phone"] = "+90 212 555 00 00",
        ["email"] = "info@ornek.example",
        ["logo"] = null,
    };

    /// <param name="type">The document type.</param>
    /// <param name="companyId">The company; null previews with a sample company.</param>
    /// <param name="culture">Language of the document.</param>
    /// <param name="model">The type's values.</param>
    /// <param name="content">Content to use instead of the saved template (previews).</param>
    /// <param name="cancellationToken">Request aborted.</param>
    public async Task<Result<ComposedDocument>> ComposeAsync(
        DocumentTypeDefinition type,
        Guid? companyId,
        string culture,
        IReadOnlyDictionary<string, object?> model,
        DocumentContent? content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(model);
        var cultureInfo = CultureInfo.GetCultureInfo(culture);

        Guid? templateId = null;
        var revision = 0;
        if (content is null)
        {
            var template = await db.DocumentTemplates.AsNoTracking()
                .Where(t => t.DocumentType == type.Key && t.Culture == cultureInfo.Name && t.IsActive && (t.CompanyId == companyId || t.CompanyId == null))
                .OrderBy(t => t.CompanyId == null)
                .FirstOrDefaultAsync(cancellationToken);
            content = template is null ? new DocumentContent(type.DefaultBody, type.DefaultCss) : new DocumentContent(template.Body, template.Css);
            templateId = template?.Id.Value;
            revision = template?.Revision ?? 0;
        }

        var company = companyId is { } id ? await CompanyAsync(id, cancellationToken) : null;
        if (companyId is not null && company is null)
        {
            return Error.Validation([new FieldError("companyId", CommonErrors.NotFound.Code)]);
        }

        var name = localizer.Get(type.NameKey, culture: cultureInfo.Name);
        var values = new Dictionary<string, object?>(model, StringComparer.Ordinal)
        {
            ["company"] = company ?? SampleCompany,
            ["document"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = type.Key,
                ["name"] = name,
                ["date"] = Today(company).ToString(DatePattern(cultureInfo), cultureInfo),
                ["revision"] = revision,
            },
            ["texts"] = Texts(type, cultureInfo.Name),
        };

        var body = renderer.Render(content.Body, values, new TemplateRenderOptions(Html: true, cultureInfo, Partials));
        if (body.IsFailure)
        {
            return Error.Validation([new FieldError(BodyField, body.Error.Code, body.Error.Args)]);
        }

        var html = $"""
            <!DOCTYPE html>
            <html lang="{cultureInfo.Name}">
            <head>
            <meta charset="utf-8">
            <title>{WebUtility.HtmlEncode(name)}</title>
            <style>{BaseCss}</style>
            <style>{content.Css}</style>
            </head>
            <body>
            {body.Value}
            </body>
            </html>
            """;
        return new ComposedDocument(html, templateId, revision);
    }

    /// <summary>The common texts, then the type's own (they win), in the document's language.</summary>
    private Dictionary<string, object?> Texts(DocumentTypeDefinition type, string culture)
    {
        var texts = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prefix in new[] { CommonTexts, DocumentTypeRegistry.TextsPrefix(type.Key) })
        {
            foreach (var (key, text) in localizer.GetAll(prefix, culture))
            {
                texts[key[prefix.Length..]] = text;
            }
        }

        return texts;
    }

    private async Task<Dictionary<string, object?>?> CompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var company = await db.Companies.AsNoTracking().SingleOrDefaultAsync(c => c.Id == CompanyId.From(companyId), cancellationToken);
        if (company is null)
        {
            return null;
        }

        var address = company.Address;
        List<object?> addressLines = address is null
            ? []
            : [.. new[]
            {
                address.Line1,
                address.Line2,
                string.Join(" ", new[] { address.PostalCode, string.Join(" / ", new[] { address.District, address.City }.Where(s => !string.IsNullOrWhiteSpace(s))) }.Where(s => !string.IsNullOrWhiteSpace(s))),
            }.Where(s => !string.IsNullOrWhiteSpace(s))];

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["code"] = company.Code,
            ["title"] = company.Title,
            ["short_name"] = company.ShortName,
            ["tax_office"] = company.TaxOffice,
            ["tax_number"] = company.TaxNumber,
            ["mersis_number"] = company.MersisNumber,
            ["address_lines"] = addressLines,
            ["phone"] = company.Phone,
            ["email"] = company.Email,
            ["logo"] = await LogoAsync(companyId, cancellationToken),
            ["time_zone"] = company.TimeZoneId,
        };
    }

    /// <summary>The logo as a data: URI, so the PDF needs nothing from outside (D36).</summary>
    private async Task<string?> LogoAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var logo = await db.StoredFiles.AsNoTracking()
            .Where(f => f.OwnerType == PlatformFileOwners.Company && f.OwnerId == companyId && f.Purpose == StoredFilePurposes.Logo)
            .OrderByDescending(f => f.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (logo is null)
        {
            return null;
        }

        await using var stream = await files.OpenReadAsync(logo, cancellationToken);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return $"data:{logo.ContentType};base64,{Convert.ToBase64String(buffer.ToArray())}";
    }

    /// <summary>Today in the company's time zone (a document printed at 01:00 in Istanbul carries that day).</summary>
    private DateTime Today(Dictionary<string, object?>? company)
    {
        var zoneId = company?["time_zone"] as string ?? Company.DefaultTimeZoneId;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        return TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime;
    }

    /// <summary>The culture's long date without the day name: "15 Ekim 2026", "October 15, 2026".</summary>
    private static string DatePattern(CultureInfo culture) =>
        culture.DateTimeFormat.LongDatePattern.Replace("dddd", string.Empty, StringComparison.Ordinal).Trim(' ', ',');
}
