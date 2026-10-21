using System.Text.Json.Serialization;
using FluentValidation;
using FluentValidation.Results;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Validation;
using Merp.Application.Persistence;
using Merp.Modules.HR.Domain.Documents;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Documents;

/// <summary>The document types of the tenant by code; only the active ones unless asked (D38).</summary>
public sealed record GetDocumentTypesQuery(bool IncludeInactive = false) : IQuery<IReadOnlyList<EmployeeDocumentTypeItem>>, IRequirePermission
{
    public string Permission => HrPermissions.Employees.Documents.Read;
}

/// <remarks>Name is in the caller's language, Names in every language. Send Version back when updating.</remarks>
public sealed record EmployeeDocumentTypeItem(Guid Id, string Code, string Name, IReadOnlyDictionary<string, string> Names, bool IsActive, uint Version);

/// <summary>Adds a document type; needs the Tenant scope, like the job catalog (D37, D38).</summary>
public sealed record CreateDocumentTypeCommand(string? Code, IReadOnlyDictionary<string, string?>? Name) : ICommand<Guid>, IRequirePermission, IDocumentTypeFields
{
    [JsonIgnore]
    public string Permission => HrPermissions.Employees.Documents.Manage;
}

/// <summary>Renames a document type or retires it; the code never changes. Needs the Tenant scope.</summary>
public sealed record UpdateDocumentTypeCommand(uint Version, IReadOnlyDictionary<string, string?>? Name, bool IsActive) : ICommand, IRequirePermission, IDocumentTypeFields
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Employees.Documents.Manage;
}

public interface IDocumentTypeFields
{
    IReadOnlyDictionary<string, string?>? Name { get; }
}

public static class DocumentTypeErrors
{
    public const int NameMaxLength = 100;

    public static readonly Error TenantScopeRequired = Error.Forbidden("hr.document_types.errors.tenant_scope_required");
}

internal static class DocumentTypeRules
{
    public static void AddFieldRules<T>(AbstractValidator<T> validator, ILocalizationSource localization)
        where T : IDocumentTypeFields
    {
        validator.RuleFor(t => t.Name).NotNull().MustCoverDefaultCulture(localization);
        validator.RuleFor(t => t.Name).Custom((name, context) =>
        {
            if (name is null)
            {
                return;
            }

            var result = LocalizedString.Create(name);
            if (result.IsFailure)
            {
                context.AddFailure(new ValidationFailure("Name", result.Error.Code) { ErrorCode = result.Error.Code });
            }
            else if (result.Value.Values.Values.Any(text => text.Length > DocumentTypeErrors.NameMaxLength))
            {
                context.AddFailure(new ValidationFailure("Name", "common.errors.max_length")
                {
                    ErrorCode = "common.errors.max_length",
                    FormattedMessagePlaceholderValues = new Dictionary<string, object> { ["MaxLength"] = DocumentTypeErrors.NameMaxLength },
                });
            }
        });
    }

    public static async Task<bool> IsTenantWideAsync(IAccessScopeProvider scopes, string permission, CancellationToken cancellationToken) =>
        (await scopes.GetCurrentUserScopesAsync(permission, cancellationToken)).Any(s => s.Type == AccessScopeTypes.Tenant);
}

internal sealed class CreateDocumentTypeCommandValidator : AbstractValidator<CreateDocumentTypeCommand>
{
    public CreateDocumentTypeCommandValidator(ILocalizationSource localization)
    {
        RuleFor(t => t.Code).Cascade(CascadeMode.Stop).NotEmpty().MustBeValid(v => Code.Create(v, DocumentType.CodeMaxLength));
        DocumentTypeRules.AddFieldRules(this, localization);
    }
}

internal sealed class UpdateDocumentTypeCommandValidator : AbstractValidator<UpdateDocumentTypeCommand>
{
    public UpdateDocumentTypeCommandValidator(ILocalizationSource localization) => DocumentTypeRules.AddFieldRules(this, localization);
}

internal sealed class GetDocumentTypesQueryHandler(IHrDbContext db, ICurrentUser currentUser) : IQueryHandler<GetDocumentTypesQuery, IReadOnlyList<EmployeeDocumentTypeItem>>
{
    public async Task<Result<IReadOnlyList<EmployeeDocumentTypeItem>>> Handle(GetDocumentTypesQuery request, CancellationToken cancellationToken)
    {
        var types = await db.DocumentTypes.AsNoTracking()
            .Where(t => request.IncludeInactive || t.IsActive)
            .Select(t => new { Type = t, Version = EF.Property<uint>(t, ConcurrencyVersion.PropertyName) })
            .ToListAsync(cancellationToken);
        return types
            .Select(t => new EmployeeDocumentTypeItem(
                t.Type.Id.Value,
                t.Type.Code,
                t.Type.Name.Get(currentUser.Culture, "tr-TR"),
                t.Type.Name.Values,
                t.Type.IsActive,
                t.Version))
            .OrderBy(t => t.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo(currentUser.Culture), ignoreCase: true))
            .ToList();
    }
}

internal sealed class CreateDocumentTypeCommandHandler(IHrDbContext db, IAccessScopeProvider scopes) : ICommandHandler<CreateDocumentTypeCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateDocumentTypeCommand request, CancellationToken cancellationToken)
    {
        if (!await DocumentTypeRules.IsTenantWideAsync(scopes, request.Permission, cancellationToken))
        {
            return DocumentTypeErrors.TenantScopeRequired;
        }

        var code = Code.Create(request.Code, DocumentType.CodeMaxLength).Value;
        if (await db.DocumentTypes.AnyAsync(t => t.Code == code.Value, cancellationToken))
        {
            return Error.Validation([new FieldError("code", CommonErrors.Duplicate.Code)]);
        }

        var type = DocumentType.Create(code, LocalizedString.Create(request.Name).Value);
        db.DocumentTypes.Add(type);
        return type.Id.Value;
    }
}

internal sealed class UpdateDocumentTypeCommandHandler(IHrDbContext db, IAccessScopeProvider scopes) : ICommandHandler<UpdateDocumentTypeCommand>
{
    public async Task<Result> Handle(UpdateDocumentTypeCommand request, CancellationToken cancellationToken)
    {
        if (!await DocumentTypeRules.IsTenantWideAsync(scopes, request.Permission, cancellationToken))
        {
            return DocumentTypeErrors.TenantScopeRequired;
        }

        var type = await db.DocumentTypes.SingleOrDefaultAsync(t => t.Id == DocumentTypeId.From(request.Id), cancellationToken);
        if (type is null)
        {
            return CommonErrors.NotFound;
        }

        db.Entry(type).ExpectVersion(request.Version);
        type.Update(LocalizedString.Create(request.Name).Value, request.IsActive);
        return Result.Success();
    }
}
