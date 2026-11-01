using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Documents;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Application.Organization;
using Merp.Modules.Platform.Domain.Companies;
using Merp.Modules.Platform.Domain.Documents;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Documents;

// Document templates (PLAN P12, D36). Administration needs the Tenant scope, like notification templates.

/// <param name="Path">Liquid path, e.g. <c>company.title</c>.</param>
/// <param name="Description">What the value is, in the caller's language.</param>
public sealed record DocumentFieldItem(string Path, string Description);

/// <param name="Key">The type, e.g. <c>platform.letter</c>.</param>
/// <param name="Name">The type's name in the caller's language.</param>
/// <param name="DefaultBody">Built-in template, used until the tenant defines one.</param>
/// <param name="DefaultCss">Built-in style sheet.</param>
/// <param name="SampleModel">Made-up values the preview uses; <c>company</c>, <c>document</c> and <c>texts</c> are added.</param>
/// <param name="Fields">The type's variables, then the common ones.</param>
/// <param name="Partials">Names a template can render, e.g. <c>{% render 'letterhead' %}</c>.</param>
public sealed record DocumentTypeItem(
    string Key,
    string Name,
    string DefaultBody,
    string DefaultCss,
    IReadOnlyDictionary<string, object?> SampleModel,
    IReadOnlyList<DocumentFieldItem> Fields,
    IReadOnlyList<DocumentFieldItem> Partials);

/// <summary>Document types of all modules with what the editor needs.</summary>
public sealed record GetDocumentTypesQuery : IQuery<IReadOnlyList<DocumentTypeItem>>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.DocumentTemplates.Read;
}

/// <remarks>Send Version back when updating (optimistic concurrency).</remarks>
public sealed record DocumentTemplateItem(
    Guid Id,
    string DocumentType,
    string DocumentTypeName,
    Guid? CompanyId,
    string? CompanyName,
    string Culture,
    int Revision,
    bool IsActive,
    uint Version);

/// <summary>The tenant's templates, optionally of one type; the content comes with the details.</summary>
public sealed record GetDocumentTemplatesQuery(string? DocumentType) : IQuery<IReadOnlyList<DocumentTemplateItem>>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.DocumentTemplates.Read;
}

public sealed record DocumentTemplateDetails(
    Guid Id,
    string DocumentType,
    string DocumentTypeName,
    Guid? CompanyId,
    string? CompanyName,
    string Culture,
    string Body,
    string? Css,
    int Revision,
    bool IsActive,
    uint Version);

public sealed record GetDocumentTemplateQuery(Guid Id) : IQuery<DocumentTemplateDetails>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.DocumentTemplates.Read;
}

/// <summary>Adds a template for a type, a company (or all, when null) and a culture; one per combination.</summary>
public sealed record CreateDocumentTemplateCommand(string? DocumentType, Guid? CompanyId, string? Culture, string? Body, string? Css)
    : ICommand<Guid>, IRequirePermission, IDocumentTemplateContent
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.DocumentTemplates.Manage;
}

/// <summary>Changes the content (the revision goes up) or turns the template off; type, company and culture stay.</summary>
public sealed record UpdateDocumentTemplateCommand(uint Version, string? Body, string? Css, bool IsActive)
    : ICommand, IRequirePermission, IDocumentTemplateContent
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => PlatformPermissions.DocumentTemplates.Manage;
}

/// <summary>Removes a template; the next more general one, or the built-in one, applies again.</summary>
public sealed record DeleteDocumentTemplateCommand(Guid Id) : ICommand, IRequirePermission
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.DocumentTemplates.Manage;
}

/// <summary>
/// Renders the type's sample model without saving: with the given content, or with the template that would apply
/// when Body is null. CompanyId null uses a sample company. Template errors come on the <c>body</c> field with the line.
/// </summary>
public sealed record PreviewDocumentQuery(string? DocumentType, Guid? CompanyId, string? Culture, string? Body, string? Css)
    : IQuery<DocumentPreview>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.DocumentTemplates.Read;
}

/// <param name="Html">The page as it goes to PDF conversion.</param>
public sealed record DocumentPreview(string Html);

/// <summary>The preview as PDF, through the converter a real document uses.</summary>
public sealed record PreviewDocumentPdfQuery(string? DocumentType, Guid? CompanyId, string? Culture, string? Body, string? Css)
    : IQuery<byte[]>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.DocumentTemplates.Read;
}

public interface IDocumentTemplateContent
{
    string? Body { get; }

    string? Css { get; }
}

internal static class DocumentTemplateRules
{
    public static void AddContentRules<T>(AbstractValidator<T> validator)
        where T : IDocumentTemplateContent
    {
        validator.RuleFor(c => c.Body).NotEmpty().MaximumLength(DocumentTemplate.BodyMaxLength);
        validator.RuleFor(c => c.Css).MaximumLength(DocumentTemplate.CssMaxLength);
    }

    /// <summary>
    /// The content renders the type's sample model, partials included: a template that does not parse, names an
    /// unknown partial or never ends is refused with its line (D36).
    /// </summary>
    public static async Task<Result> CheckAsync(DocumentComposer composer, DocumentTypeDefinition type, string culture, IDocumentTemplateContent content, CancellationToken cancellationToken)
    {
        var rendered = await composer.ComposeAsync(type, companyId: null, culture, type.SampleModel, new DocumentContent(content.Body!, content.Css), cancellationToken);
        return rendered.IsSuccess ? Result.Success() : rendered.Error;
    }

    /// <summary>The culture of the request, the default one when none is given; null when it is not active.</summary>
    public static async Task<string?> CultureAsync(ILocalizationSource localization, string? culture, CancellationToken cancellationToken)
    {
        var snapshot = await localization.GetAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(culture) ? snapshot.DefaultCulture : snapshot.Find(culture)?.Code;
    }
}

internal sealed class CreateDocumentTemplateCommandValidator : AbstractValidator<CreateDocumentTemplateCommand>
{
    public CreateDocumentTemplateCommandValidator(DocumentTypeRegistry types)
    {
        RuleFor(c => c.DocumentType).Cascade(CascadeMode.Stop).NotEmpty().Must(type => types.Find(type) is not null).WithErrorCode(DocumentErrors.UnknownTypeCode);
        RuleFor(c => c.Culture).NotEmpty();
        DocumentTemplateRules.AddContentRules(this);
    }
}

internal sealed class UpdateDocumentTemplateCommandValidator : AbstractValidator<UpdateDocumentTemplateCommand>
{
    public UpdateDocumentTemplateCommandValidator() => DocumentTemplateRules.AddContentRules(this);
}

internal sealed class PreviewDocumentQueryValidator : AbstractValidator<PreviewDocumentQuery>
{
    public PreviewDocumentQueryValidator(DocumentTypeRegistry types)
    {
        RuleFor(c => c.DocumentType).Cascade(CascadeMode.Stop).NotEmpty().Must(type => types.Find(type) is not null).WithErrorCode(DocumentErrors.UnknownTypeCode);
        RuleFor(c => c.Body).MaximumLength(DocumentTemplate.BodyMaxLength);
        RuleFor(c => c.Css).MaximumLength(DocumentTemplate.CssMaxLength);
    }
}

internal sealed class PreviewDocumentPdfQueryValidator : AbstractValidator<PreviewDocumentPdfQuery>
{
    public PreviewDocumentPdfQueryValidator(DocumentTypeRegistry types)
    {
        RuleFor(c => c.DocumentType).Cascade(CascadeMode.Stop).NotEmpty().Must(type => types.Find(type) is not null).WithErrorCode(DocumentErrors.UnknownTypeCode);
        RuleFor(c => c.Body).MaximumLength(DocumentTemplate.BodyMaxLength);
        RuleFor(c => c.Css).MaximumLength(DocumentTemplate.CssMaxLength);
    }
}

internal sealed class GetDocumentTypesQueryHandler(DocumentTypeRegistry types, IAccessScopeProvider scopes, ITextLocalizer localizer)
    : IQueryHandler<GetDocumentTypesQuery, IReadOnlyList<DocumentTypeItem>>
{
    /// <summary>Values Platform adds to every document.</summary>
    internal static readonly string[] CommonFields =
    [
        "company.title", "company.short_name", "company.code", "company.tax_office", "company.tax_number", "company.mersis_number",
        "company.address_lines", "company.phone", "company.email", "company.logo", "document.name", "document.date", "document.revision",
    ];

    public async Task<Result<IReadOnlyList<DocumentTypeItem>>> Handle(GetDocumentTypesQuery request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return DocumentErrors.TenantScopeRequired;
        }

        IReadOnlyList<DocumentFieldItem> common = [.. CommonFields.Select(path => new DocumentFieldItem(path, localizer.Get($"platform.documents.fields.{path.Replace('.', '_')}")))];
        IReadOnlyList<DocumentFieldItem> partials =
        [
            new("logo", localizer.Get("platform.documents.partials.logo")),
            new("letterhead", localizer.Get("platform.documents.partials.letterhead")),
        ];

        IReadOnlyList<DocumentTypeItem> items =
        [
            .. types.All
                .Select(t => new DocumentTypeItem(
                    t.Key,
                    localizer.Get(t.NameKey),
                    t.DefaultBody,
                    t.DefaultCss,
                    t.SampleModel,
                    [.. t.Fields.Select(f => new DocumentFieldItem(f.Path, localizer.Get(f.DescriptionKey))), .. common],
                    partials))
                .OrderBy(t => t.Name, StringComparer.CurrentCulture),
        ];
        return Result.Success(items);
    }
}

internal sealed class GetDocumentTemplatesQueryHandler(IPlatformDbContext db, IAccessScopeProvider scopes, DocumentTypeRegistry types, ITextLocalizer localizer)
    : IQueryHandler<GetDocumentTemplatesQuery, IReadOnlyList<DocumentTemplateItem>>
{
    public async Task<Result<IReadOnlyList<DocumentTemplateItem>>> Handle(GetDocumentTemplatesQuery request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return DocumentErrors.TenantScopeRequired;
        }

        var query = db.DocumentTemplates.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.DocumentType))
        {
            query = query.Where(t => t.DocumentType == request.DocumentType);
        }

        var rows = await query
            .Select(t => new
            {
                t.Id,
                t.DocumentType,
                t.CompanyId,
                t.Culture,
                t.Revision,
                t.IsActive,
                Version = EF.Property<uint>(t, ConcurrencyVersion.PropertyName),
            })
            .ToListAsync(cancellationToken);
        var companies = await DocumentTemplateNames.CompaniesAsync(db, rows.Select(r => r.CompanyId), cancellationToken);

        IReadOnlyList<DocumentTemplateItem> items =
        [
            .. rows
                .Select(r => new DocumentTemplateItem(
                    r.Id.Value,
                    r.DocumentType,
                    DocumentTemplateNames.Type(types, localizer, r.DocumentType),
                    r.CompanyId,
                    r.CompanyId is { } id ? companies.GetValueOrDefault(id) : null,
                    r.Culture,
                    r.Revision,
                    r.IsActive,
                    r.Version))
                .OrderBy(t => t.DocumentTypeName, StringComparer.CurrentCulture)
                .ThenBy(t => t.CompanyName ?? string.Empty, StringComparer.CurrentCulture)
                .ThenBy(t => t.Culture, StringComparer.Ordinal),
        ];
        return Result.Success(items);
    }
}

internal sealed class GetDocumentTemplateQueryHandler(IPlatformDbContext db, IAccessScopeProvider scopes, DocumentTypeRegistry types, ITextLocalizer localizer)
    : IQueryHandler<GetDocumentTemplateQuery, DocumentTemplateDetails>
{
    public async Task<Result<DocumentTemplateDetails>> Handle(GetDocumentTemplateQuery request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return DocumentErrors.TenantScopeRequired;
        }

        var row = await db.DocumentTemplates.AsNoTracking()
            .Where(t => t.Id == DocumentTemplateId.From(request.Id))
            .Select(t => new { Template = t, Version = EF.Property<uint>(t, ConcurrencyVersion.PropertyName) })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return CommonErrors.NotFound;
        }

        var t = row.Template;
        var companies = await DocumentTemplateNames.CompaniesAsync(db, [t.CompanyId], cancellationToken);
        return new DocumentTemplateDetails(
            t.Id.Value,
            t.DocumentType,
            DocumentTemplateNames.Type(types, localizer, t.DocumentType),
            t.CompanyId,
            t.CompanyId is { } id ? companies.GetValueOrDefault(id) : null,
            t.Culture,
            t.Body,
            t.Css,
            t.Revision,
            t.IsActive,
            row.Version);
    }
}

internal sealed class CreateDocumentTemplateCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    ILocalizationSource localization,
    DocumentTypeRegistry types,
    DocumentComposer composer) : ICommandHandler<CreateDocumentTemplateCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateDocumentTemplateCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return DocumentErrors.TenantScopeRequired;
        }

        var errors = new List<FieldError>();
        var culture = await DocumentTemplateRules.CultureAsync(localization, request.Culture, cancellationToken);
        if (culture is null)
        {
            errors.Add(new FieldError("culture", CommonErrors.InvalidCulture.Code));
        }

        if (request.CompanyId is { } companyId && !await db.Companies.AnyAsync(c => c.Id == CompanyId.From(companyId), cancellationToken))
        {
            errors.Add(new FieldError("companyId", CommonErrors.NotFound.Code));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var exists = await db.DocumentTemplates.AnyAsync(
            t => t.DocumentType == request.DocumentType && t.CompanyId == request.CompanyId && t.Culture == culture,
            cancellationToken);
        if (exists)
        {
            return Error.Validation([OrganizationErrors.Duplicate("documentType")]);
        }

        var check = await DocumentTemplateRules.CheckAsync(composer, types.Find(request.DocumentType)!, culture!, request, cancellationToken);
        if (check.IsFailure)
        {
            return check.Error;
        }

        var template = DocumentTemplate.Create(request.DocumentType!, request.CompanyId, culture!, request.Body!, request.Css);
        db.DocumentTemplates.Add(template);
        return template.Id.Value;
    }
}

internal sealed class UpdateDocumentTemplateCommandHandler(IPlatformDbContext db, IAccessScopeProvider scopes, DocumentTypeRegistry types, DocumentComposer composer)
    : ICommandHandler<UpdateDocumentTemplateCommand>
{
    public async Task<Result> Handle(UpdateDocumentTemplateCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return DocumentErrors.TenantScopeRequired;
        }

        var template = await db.DocumentTemplates.SingleOrDefaultAsync(t => t.Id == DocumentTemplateId.From(request.Id), cancellationToken);
        if (template is null)
        {
            return CommonErrors.NotFound;
        }

        // A template of a type no module declares any more is still editable, but there is no sample to check against
        if (types.Find(template.DocumentType) is { } type)
        {
            var check = await DocumentTemplateRules.CheckAsync(composer, type, template.Culture, request, cancellationToken);
            if (check.IsFailure)
            {
                return check.Error;
            }
        }

        db.Entry(template).ExpectVersion(request.Version);
        template.Update(request.Body!, request.Css, request.IsActive);
        return Result.Success();
    }
}

internal sealed class DeleteDocumentTemplateCommandHandler(IPlatformDbContext db, IAccessScopeProvider scopes)
    : ICommandHandler<DeleteDocumentTemplateCommand>
{
    public async Task<Result> Handle(DeleteDocumentTemplateCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return DocumentErrors.TenantScopeRequired;
        }

        var template = await db.DocumentTemplates.SingleOrDefaultAsync(t => t.Id == DocumentTemplateId.From(request.Id), cancellationToken);
        if (template is null)
        {
            return CommonErrors.NotFound;
        }

        db.DocumentTemplates.Remove(template);
        return Result.Success();
    }
}

internal sealed class PreviewDocumentQueryHandler(IAccessScopeProvider scopes, ILocalizationSource localization, DocumentTypeRegistry types, DocumentComposer composer)
    : IQueryHandler<PreviewDocumentQuery, DocumentPreview>
{
    public async Task<Result<DocumentPreview>> Handle(PreviewDocumentQuery request, CancellationToken cancellationToken)
    {
        var page = await DocumentPreviews.ComposeAsync(scopes, localization, types, composer, request.Permission, request.DocumentType, request.CompanyId, request.Culture, request.Body, request.Css, cancellationToken);
        return page.IsSuccess ? new DocumentPreview(page.Value.Html) : page.Error;
    }
}

internal sealed class PreviewDocumentPdfQueryHandler(
    IAccessScopeProvider scopes,
    ILocalizationSource localization,
    DocumentTypeRegistry types,
    DocumentComposer composer,
    IPdfConverter pdf) : IQueryHandler<PreviewDocumentPdfQuery, byte[]>
{
    public async Task<Result<byte[]>> Handle(PreviewDocumentPdfQuery request, CancellationToken cancellationToken)
    {
        var page = await DocumentPreviews.ComposeAsync(scopes, localization, types, composer, request.Permission, request.DocumentType, request.CompanyId, request.Culture, request.Body, request.Css, cancellationToken);
        return page.IsSuccess ? await pdf.ConvertHtmlAsync(page.Value.Html, cancellationToken) : page.Error;
    }
}

internal static class DocumentPreviews
{
    public static async Task<Result<ComposedDocument>> ComposeAsync(
        IAccessScopeProvider scopes,
        ILocalizationSource localization,
        DocumentTypeRegistry types,
        DocumentComposer composer,
        string permission,
        string? documentType,
        Guid? companyId,
        string? culture,
        string? body,
        string? css,
        CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, permission, cancellationToken))
        {
            return DocumentErrors.TenantScopeRequired;
        }

        var code = await DocumentTemplateRules.CultureAsync(localization, culture, cancellationToken);
        if (code is null)
        {
            return Error.Validation([new FieldError("culture", CommonErrors.InvalidCulture.Code)]);
        }

        var type = types.Find(documentType)!;
        var content = string.IsNullOrWhiteSpace(body) ? null : new DocumentContent(body, css);
        return await composer.ComposeAsync(type, companyId, code, type.SampleModel, content, cancellationToken);
    }
}

internal static class DocumentTemplateNames
{
    public static string Type(DocumentTypeRegistry types, ITextLocalizer localizer, string key) =>
        types.Find(key) is { } type ? localizer.Get(type.NameKey) : key;

    public static async Task<Dictionary<Guid, string>> CompaniesAsync(IPlatformDbContext db, IEnumerable<Guid?> companyIds, CancellationToken cancellationToken)
    {
        var ids = companyIds.OfType<Guid>().Select(CompanyId.From).Distinct().ToList();
        return ids.Count == 0
            ? []
            : await db.Companies.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id.Value, c => c.ShortName, cancellationToken);
    }
}
