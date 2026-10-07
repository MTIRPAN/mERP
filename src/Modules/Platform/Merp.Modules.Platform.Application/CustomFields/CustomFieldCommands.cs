using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.CustomFields;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Validation;
using Merp.Application.Persistence;
using Merp.Modules.Platform.Application.Access;
using Merp.Modules.Platform.Domain.Companies;
using Merp.Modules.Platform.Domain.CustomFields;
using Merp.SharedKernel.CustomFields;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.CustomFields;

/// <summary>Entities of the Platform module with custom fields (PLAN P6).</summary>
public sealed class PlatformCustomFieldEntities : ICustomFieldEntityProvider
{
    public const string Company = "platform.company";

    public IEnumerable<CustomFieldEntityType> GetEntityTypes() =>
    [
        new(Company, "platform.custom_fields.entities.company"),
    ];
}

/// <summary>Clears the tenant's cached definitions; call it after the command commits.</summary>
public interface ICustomFieldCache
{
    Task InvalidateAsync(CancellationToken cancellationToken = default);
}

/// <summary>An option as entered: code and label per culture.</summary>
public sealed record CustomFieldOptionInput(string? Code, IReadOnlyDictionary<string, string?>? Label);

/// <summary>Settings that may change after creation (D30). Labels hold a text per culture.</summary>
public interface ICustomFieldInput
{
    IReadOnlyDictionary<string, string?>? Label { get; }

    bool Required { get; }

    IReadOnlyList<CustomFieldOptionInput>? Options { get; }

    int? MaxLength { get; }

    decimal? Min { get; }

    decimal? Max { get; }

    bool WholeNumber { get; }

    IReadOnlyDictionary<string, string?>? Group { get; }

    int Order { get; }
}

/// <summary>
/// Defines a custom field of an entity type, for every company or one. The code, data type and company never change.
/// </summary>
public sealed record CreateCustomFieldCommand(
    string? EntityType,
    string? Code,
    CustomFieldType Type,
    Guid? CompanyId,
    IReadOnlyDictionary<string, string?>? Label,
    bool Required,
    IReadOnlyList<CustomFieldOptionInput>? Options,
    int? MaxLength,
    decimal? Min,
    decimal? Max,
    bool WholeNumber,
    IReadOnlyDictionary<string, string?>? Group,
    int Order) : ICommand<Guid>, IRequirePermission, ICustomFieldInput
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.CustomFields.Manage;
}

/// <summary>Changes a definition; IsActive false hides the field and keeps the stored values.</summary>
public sealed record UpdateCustomFieldCommand(
    uint Version,
    IReadOnlyDictionary<string, string?>? Label,
    bool Required,
    IReadOnlyList<CustomFieldOptionInput>? Options,
    int? MaxLength,
    decimal? Min,
    decimal? Max,
    bool WholeNumber,
    IReadOnlyDictionary<string, string?>? Group,
    int Order,
    bool IsActive) : ICommand, IRequirePermission, ICustomFieldInput
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => PlatformPermissions.CustomFields.Manage;
}

public static class CustomFieldErrors
{
    public static readonly Error TenantScopeRequired = Error.Forbidden("platform.custom_fields.errors.tenant_scope_required");
    public const string UnknownEntityTypeCode = "platform.custom_fields.errors.unknown_entity_type";
    public const string UnknownCompanyCode = "platform.custom_fields.errors.unknown_company";
}

internal static class CustomFieldRules
{
    public static void AddInputRules<T>(AbstractValidator<T> validator, ILocalizationSource localization)
        where T : ICustomFieldInput
    {
        validator.RuleFor(c => c.Label).Custom((label, context) =>
        {
            if (LocalizedString.Create(label) is { IsFailure: true } result)
            {
                context.AddFailure(new FluentValidation.Results.ValidationFailure("Label", result.Error.Code) { ErrorCode = result.Error.Code });
            }
        });
        validator.RuleFor(c => c.Label).MustCoverDefaultCulture(localization);
        validator.RuleFor(c => c.Group).MustCoverDefaultCulture(localization);
        validator.RuleForEach(c => c.Options).ChildRules(option => option.RuleFor(o => o.Label).MustCoverDefaultCulture(localization))
            .OverridePropertyName("options");
    }

    /// <summary>Input → settings; option and group labels without text are dropped (the domain rejects empty options).</summary>
    public static CustomFieldSettings ToSettings(ICustomFieldInput input, bool isActive) => new(
        LocalizedString.Create(input.Label).Value,
        input.Required,
        [.. (input.Options ?? []).Select(o => new CustomFieldOption(
            o.Code?.Trim() ?? string.Empty,
            LocalizedString.Create(o.Label) is { IsSuccess: true } label ? label.Value.Values : new Dictionary<string, string>()))],
        input.MaxLength,
        input.Min,
        input.Max,
        input.WholeNumber,
        LocalizedString.Create(input.Group) is { IsSuccess: true } group ? group.Value : null,
        input.Order,
        isActive);
}

internal sealed class CreateCustomFieldCommandValidator : AbstractValidator<CreateCustomFieldCommand>
{
    public CreateCustomFieldCommandValidator(ILocalizationSource localization)
    {
        RuleFor(c => c.EntityType).NotEmpty();
        RuleFor(c => c.Code).Cascade(CascadeMode.Stop).NotEmpty()
            .Must(CustomFieldDefinition.IsValidCode).WithErrorCode(CustomFieldDefinition.InvalidCodeCode);
        RuleFor(c => c.Type).IsInEnum();
        CustomFieldRules.AddInputRules(this, localization);
    }
}

internal sealed class UpdateCustomFieldCommandValidator : AbstractValidator<UpdateCustomFieldCommand>
{
    public UpdateCustomFieldCommandValidator(ILocalizationSource localization) => CustomFieldRules.AddInputRules(this, localization);
}

internal sealed class CreateCustomFieldCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    IEnumerable<ICustomFieldEntityProvider> entities,
    ICustomFieldCache cache,
    IAfterCommit afterCommit) : ICommandHandler<CreateCustomFieldCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateCustomFieldCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return CustomFieldErrors.TenantScopeRequired;
        }

        var errors = new List<FieldError>();
        var entityType = request.EntityType!.Trim();
        if (!entities.SelectMany(p => p.GetEntityTypes()).Any(e => e.Code == entityType))
        {
            errors.Add(new FieldError("entityType", CustomFieldErrors.UnknownEntityTypeCode));
        }

        if (request.CompanyId is { } companyId && !await db.Companies.AnyAsync(c => c.Id == CompanyId.From(companyId), cancellationToken))
        {
            errors.Add(new FieldError("companyId", CustomFieldErrors.UnknownCompanyCode));
        }

        // One code per entity type, whichever company it is for: a record's values are keyed by code
        if (await db.CustomFieldDefinitions.AnyAsync(d => d.EntityType == entityType && d.Code == request.Code, cancellationToken))
        {
            errors.Add(new FieldError("code", CommonErrors.Duplicate.Code));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var definition = CustomFieldDefinition.Create(entityType, request.Code!, request.Type, request.CompanyId, CustomFieldRules.ToSettings(request, isActive: true));
        if (definition.IsFailure)
        {
            return definition.Error;
        }

        db.CustomFieldDefinitions.Add(definition.Value);
        afterCommit.Enqueue(cache.InvalidateAsync);
        return definition.Value.Id.Value;
    }
}

internal sealed class UpdateCustomFieldCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    ICustomFieldCache cache,
    IAfterCommit afterCommit) : ICommandHandler<UpdateCustomFieldCommand>
{
    public async Task<Result> Handle(UpdateCustomFieldCommand request, CancellationToken cancellationToken)
    {
        if (!await TenantAdministration.IsAllowedAsync(scopes, request.Permission, cancellationToken))
        {
            return CustomFieldErrors.TenantScopeRequired;
        }

        var definition = await db.CustomFieldDefinitions.SingleOrDefaultAsync(d => d.Id == CustomFieldDefinitionId.From(request.Id), cancellationToken);
        if (definition is null)
        {
            return CommonErrors.NotFound;
        }

        db.Entry(definition).ExpectVersion(request.Version);
        var updated = definition.Update(CustomFieldRules.ToSettings(request, request.IsActive));
        if (updated.IsSuccess)
        {
            afterCommit.Enqueue(cache.InvalidateAsync);
        }

        return updated;
    }
}
