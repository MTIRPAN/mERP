using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.CustomFields;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Persistence;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Validation;
using Merp.Modules.Platform.Application.CustomFields;
using Merp.Modules.Platform.Application.Organization;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.Modules.Platform.Domain.Companies;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Companies;

/// <summary>Creates a company. Needs <c>platform.companies.manage</c> with the Tenant scope.</summary>
/// <remarks>CustomFields holds the values of the tenant's custom fields by code (PLAN P6).</remarks>
public sealed record CreateCompanyCommand(
    string? Code,
    string? Title,
    string? ShortName,
    string? TaxOffice,
    string? TaxNumber,
    string? MersisNumber,
    string? SgkWorkplaceNumber,
    AddressInput? Address,
    string? Phone,
    string? Email,
    string? Culture,
    string? TimeZoneId,
    IReadOnlyDictionary<string, JsonElement>? CustomFields = null) : ICommand<Guid>, IRequirePermission, ICompanyFields
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.Companies.Manage;
}

/// <summary>Updates a company's details and active flag. The code never changes.</summary>
/// <remarks>Version is the one the client read; a concurrent change ends in 409.</remarks>
public sealed record UpdateCompanyCommand(
    uint Version,
    string? Title,
    string? ShortName,
    string? TaxOffice,
    string? TaxNumber,
    string? MersisNumber,
    string? SgkWorkplaceNumber,
    AddressInput? Address,
    string? Phone,
    string? Email,
    string? Culture,
    string? TimeZoneId,
    bool IsActive,
    IReadOnlyDictionary<string, JsonElement>? CustomFields = null) : ICommand, IRequirePermission, ICompanyFields
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => PlatformPermissions.Companies.Manage;
}

/// <summary>Fields shared by create and update, validated by one rule set.</summary>
public interface ICompanyFields
{
    string? Title { get; }

    string? ShortName { get; }

    string? TaxOffice { get; }

    string? TaxNumber { get; }

    string? MersisNumber { get; }

    string? SgkWorkplaceNumber { get; }

    string? Phone { get; }

    string? Email { get; }

    string? Culture { get; }

    string? TimeZoneId { get; }
}

internal static class CompanyRules
{
    /// <summary>Entity type of companies for custom fields (PLAN P6).</summary>
    public const string EntityType = PlatformCustomFieldEntities.Company;

    public static void AddFieldRules<T>(AbstractValidator<T> validator)
        where T : ICompanyFields
    {
        validator.RuleFor(c => c.Title).NotEmpty().MaximumLength(Company.TitleMaxLength);
        validator.RuleFor(c => c.ShortName).NotEmpty().MaximumLength(Company.ShortNameMaxLength);
        validator.RuleFor(c => c.TaxOffice).MaximumLength(Company.TaxOfficeMaxLength);
        validator.RuleFor(c => c.TaxNumber).Cascade(CascadeMode.Stop).NotEmpty().MustBeValid(Vkn.Create);
        validator.RuleFor(c => c.MersisNumber).MustBeValid(MersisNumber.Create).When(c => !string.IsNullOrWhiteSpace(c.MersisNumber));
        validator.RuleFor(c => c.SgkWorkplaceNumber).MustBeValid(SgkWorkplaceNumber.Create).When(c => !string.IsNullOrWhiteSpace(c.SgkWorkplaceNumber));
        validator.RuleFor(c => c.Phone).MustBeValid(PhoneNumber.Create).When(c => !string.IsNullOrWhiteSpace(c.Phone));
        validator.RuleFor(c => c.Email).MustBeValid(Email.Create).When(c => !string.IsNullOrWhiteSpace(c.Email));
        validator.RuleFor(c => c.Culture).Must(Company.IsKnownCulture).WithErrorCode(CommonErrors.InvalidCulture.Code)
            .When(c => !string.IsNullOrWhiteSpace(c.Culture));
        validator.RuleFor(c => c.TimeZoneId).Must(Company.IsKnownTimeZone).WithErrorCode(CommonErrors.InvalidTimeZone.Code)
            .When(c => !string.IsNullOrWhiteSpace(c.TimeZoneId));
    }

    /// <summary>Validated fields as a domain profile (the validator has run, so the value objects are valid).</summary>
    public static CompanyProfile ToProfile(ICompanyFields fields, Address? address) =>
        new(
            fields.Title!,
            fields.ShortName!,
            fields.TaxOffice,
            Vkn.Create(fields.TaxNumber).Value,
            Optional(fields.MersisNumber, SharedKernel.ValueObjects.MersisNumber.Create),
            Optional(fields.SgkWorkplaceNumber, SharedKernel.ValueObjects.SgkWorkplaceNumber.Create),
            address,
            Optional(fields.Phone, PhoneNumber.Create),
            Optional(fields.Email, SharedKernel.ValueObjects.Email.Create),
            string.IsNullOrWhiteSpace(fields.Culture) ? Company.DefaultCulture : fields.Culture.Trim(),
            string.IsNullOrWhiteSpace(fields.TimeZoneId) ? Company.DefaultTimeZoneId : fields.TimeZoneId.Trim());

    public static async Task<Result<Address?>> ResolveAddressAsync(AddressResolver resolver, AddressInput? input, CancellationToken cancellationToken)
    {
        if (input is null)
        {
            return Result.Success<Address?>(null);
        }

        var address = await resolver.ResolveAsync(input, cancellationToken);
        return address.IsSuccess ? Result.Success<Address?>(address.Value) : Result.Failure<Address?>(address.Error);
    }

    /// <summary>The tax number must be unique in the tenant.</summary>
    public static async Task<FieldError?> CheckTaxNumberAsync(IPlatformDbContext db, string taxNumber, CompanyId? except, CancellationToken cancellationToken)
    {
        var normalized = Vkn.Create(taxNumber).Value.Value;
        var companies = db.Companies.Where(c => c.TaxNumber == normalized);
        if (except is { } self)
        {
            companies = companies.Where(c => c.Id != self);
        }

        var taken = await companies.AnyAsync(cancellationToken);
        return taken ? OrganizationErrors.Duplicate("taxNumber") : null;
    }

    public static CompanyUpdated ToUpdatedEvent(Company company) =>
        new CompanyUpdated(company.Code, company.ShortName, company.TimeZoneId, company.IsActive) { CompanyId = company.Id.Value };

    private static TValue? Optional<TValue>(string? value, Func<string?, Result<TValue>> create)
        where TValue : class =>
        string.IsNullOrWhiteSpace(value) ? null : create(value).Value;
}

internal sealed class CreateCompanyCommandValidator : AbstractValidator<CreateCompanyCommand>
{
    public CreateCompanyCommandValidator()
    {
        RuleFor(c => c.Code).Cascade(CascadeMode.Stop).NotEmpty().MustBeValid(v => SharedKernel.ValueObjects.Code.Create(v, Company.CodeMaxLength));
        CompanyRules.AddFieldRules(this);
    }
}

internal sealed class UpdateCompanyCommandValidator : AbstractValidator<UpdateCompanyCommand>
{
    public UpdateCompanyCommandValidator() => CompanyRules.AddFieldRules(this);
}

internal sealed class CreateCompanyCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    AddressResolver addressResolver,
    ICustomFieldValidator customFields,
    IIntegrationEventPublisher publisher) : ICommandHandler<CreateCompanyCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateCompanyCommand request, CancellationToken cancellationToken)
    {
        var scope = OrganizationScope.From(await scopes.GetCurrentUserScopesAsync(request.Permission, cancellationToken));
        if (!scope.IsTenantWide)
        {
            return OrganizationErrors.CompanyCreationNeedsTenantScope;
        }

        var code = SharedKernel.ValueObjects.Code.Create(request.Code, Company.CodeMaxLength).Value;
        var errors = new List<FieldError>();
        if (await db.Companies.AnyAsync(c => c.Code == code.Value, cancellationToken))
        {
            errors.Add(OrganizationErrors.Duplicate("code"));
        }

        if (await CompanyRules.CheckTaxNumberAsync(db, request.TaxNumber!, null, cancellationToken) is { } taxNumberTaken)
        {
            errors.Add(taxNumberTaken);
        }

        var address = await CompanyRules.ResolveAddressAsync(addressResolver, request.Address, cancellationToken);
        if (address.IsFailure)
        {
            errors.AddRange(address.Error.Fields);
        }

        // A new company has no company specific fields yet
        var values = await customFields.ValidateAsync(CompanyRules.EntityType, null, request.CustomFields, CustomFieldValues.Empty, cancellationToken);
        if (values.IsFailure)
        {
            errors.AddRange(values.Error.Fields);
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var company = Company.Create(code, CompanyRules.ToProfile(request, address.Value));
        company.SetCustomFields(values.Value);
        db.Companies.Add(company);
        await publisher.PublishAsync(
            new CompanyCreated(company.Code, company.ShortName, company.TimeZoneId, company.IsActive) { CompanyId = company.Id.Value },
            cancellationToken);
        return company.Id.Value;
    }
}

internal sealed class UpdateCompanyCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    AddressResolver addressResolver,
    ICustomFieldValidator customFields,
    IIntegrationEventPublisher publisher) : ICommandHandler<UpdateCompanyCommand>
{
    public async Task<Result> Handle(UpdateCompanyCommand request, CancellationToken cancellationToken)
    {
        var id = CompanyId.From(request.Id);
        var scope = OrganizationScope.From(await scopes.GetCurrentUserScopesAsync(request.Permission, cancellationToken));
        var company = scope.CoversCompany(id) ? await db.Companies.SingleOrDefaultAsync(c => c.Id == id, cancellationToken) : null;
        if (company is null)
        {
            // Outside the caller's scope looks the same as missing (docs/domain/yetki-ve-onay.md §1.3)
            return CommonErrors.NotFound;
        }

        var errors = new List<FieldError>();
        if (await CompanyRules.CheckTaxNumberAsync(db, request.TaxNumber!, id, cancellationToken) is { } taxNumberTaken)
        {
            errors.Add(taxNumberTaken);
        }

        var address = await CompanyRules.ResolveAddressAsync(addressResolver, request.Address, cancellationToken);
        if (address.IsFailure)
        {
            errors.AddRange(address.Error.Fields);
        }

        var values = await customFields.ValidateAsync(CompanyRules.EntityType, company.Id.Value, request.CustomFields, company.CustomFields, cancellationToken);
        if (values.IsFailure)
        {
            errors.AddRange(values.Error.Fields);
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(company).ExpectVersion(request.Version);
        company.Update(CompanyRules.ToProfile(request, address.Value));
        company.SetCustomFields(values.Value);
        if (request.IsActive)
        {
            company.Activate();
        }
        else
        {
            company.Deactivate();
        }

        await publisher.PublishAsync(CompanyRules.ToUpdatedEvent(company), cancellationToken);
        return Result.Success();
    }
}
