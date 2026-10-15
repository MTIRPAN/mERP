using System.Globalization;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;

namespace Merp.Modules.Platform.Domain.Documents;

public readonly record struct DocumentTemplateId(Guid Value) : IEntityId<DocumentTemplateId>
{
    public static DocumentTemplateId From(Guid value) => new(value);
}

/// <summary>
/// A tenant's template for one document type (PLAN P12, D36), for one company or all of them, in one culture. The
/// company's template comes before the one for all companies; without any, the type's built-in template is used.
/// </summary>
[Audited]
public sealed class DocumentTemplate : AuditableAggregateRoot<DocumentTemplateId>
{
    public const int DocumentTypeMaxLength = 100;
    public const int BodyMaxLength = 200_000;
    public const int CssMaxLength = 50_000;

    private DocumentTemplate()
    {
    }

    /// <summary>The document type a module declared, e.g. <c>platform.letter</c>.</summary>
    public string DocumentType { get; private set; } = null!;

    /// <summary>The company, or null for every company.</summary>
    public Guid? CompanyId { get; private set; }

    public string Culture { get; private set; } = null!;

    /// <summary>Liquid, rendered as HTML.</summary>
    public string Body { get; private set; } = null!;

    /// <summary>Style sheet added to the page; null when the body needs none.</summary>
    public string? Css { get; private set; }

    /// <summary>1 when created, one more whenever the body or the style sheet changes; documents can print it.</summary>
    public int Revision { get; private set; }

    /// <summary>An inactive template is skipped: the next more general one, or the built-in one, applies.</summary>
    public bool IsActive { get; private set; }

    public static DocumentTemplate Create(string documentType, Guid? companyId, string culture, string body, string? css) =>
        new()
        {
            Id = EntityId.New<DocumentTemplateId>(),
            DocumentType = Text.Required(documentType, DocumentTypeMaxLength, nameof(documentType)),
            CompanyId = companyId,
            Culture = CultureInfo.GetCultureInfo(culture, predefinedOnly: true).Name,
            Body = Text.Required(body, BodyMaxLength, nameof(body)),
            Css = Text.Optional(css, CssMaxLength, nameof(css)),
            Revision = 1,
            IsActive = true,
        };

    /// <summary>Changes the content; the revision goes up only when something actually changed.</summary>
    public void Update(string body, string? css, bool isActive)
    {
        var newBody = Text.Required(body, BodyMaxLength, nameof(body));
        var newCss = Text.Optional(css, CssMaxLength, nameof(css));
        if (!string.Equals(newBody, Body, StringComparison.Ordinal) || !string.Equals(newCss, Css, StringComparison.Ordinal))
        {
            Body = newBody;
            Css = newCss;
            Revision++;
        }

        IsActive = isActive;
    }
}
