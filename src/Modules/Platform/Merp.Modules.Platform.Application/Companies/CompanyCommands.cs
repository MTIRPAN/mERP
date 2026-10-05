using System.Globalization;
using FluentValidation;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Persistence;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Validation;
using Merp.Modules.Platform.Application.Common;
using Merp.Modules.Platform.Application.Locations;
using Merp.Modules.Platform.Contracts.Companies;
using Merp.Modules.Platform.Domain.Companies;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Companies;

/// <summary>Editable fields shared by create and update (validated by <see cref="CompanyFieldRules"/>).</summary>
public interface ICompanyFields
{
    string Title { get; }

    string ShortName { get; }

    string? TaxOffice { get; }

    string TaxNumber { get; }

    string? MersisNo { get; }

    string? SgkWorkplaceNo { get; }

    string AddressLine1 { get; }

    string? AddressLine2 { get; }

    int ProvinceCode { get; }

    int DistrictId { get; }

    string? PostalCode { get; }

    string? Phone { get; }

    string? Email { get; }

    string DefaultCulture { get; }

    string TimeZoneId { get; }
}

public sealed record CreateCompanyCommand(
    string Code,
    string Title,
    string ShortName,
    string? TaxOffice,
    string TaxNumber,
    string? MersisNo,
    string? SgkWorkplaceNo,
    string AddressLine1,
    string? AddressLine2,
    int ProvinceCode,
    int DistrictId,
    string? PostalCode,
    string? Phone,
    string? Email,
    string DefaultCulture = "tr-TR",
    string TimeZoneId = Company.DefaultTimeZoneId) : ICommand<Guid>, ICompanyFields, IRequirePermission
{
    public string Permission => PlatformPermissions.Companies.Manage;
}

public sealed record UpdateCompanyCommand(
    Guid Id,
    uint Version,
    string Title,
    string ShortName,
    string? TaxOffice,
    string TaxNumber,
    string? MersisNo,
    string? SgkWorkplaceNo,
    string AddressLine1,
    string? AddressLine2,
    int ProvinceCode,
    int DistrictId,
    string? PostalCode,
    string? Phone,
    string? Email,
    string DefaultCulture,
    string TimeZoneId,
    bool IsActive) : ICommand, ICompanyFields, IRequirePermission
{
    public string Permission => PlatformPermissions.Companies.Manage;
}

/// <summary>
/// Field rules shared by create and update. An extension rather than a validator class, so the validator scan does
/// not register it as one more validator of both commands.
/// </summary>
internal static class CompanyFieldRules
{
    public static void ValidateCompanyFields<T>(this AbstractValidator<T> v)
        where T : ICompanyFields
    {
        v.RuleFor(c => c.Title).NotEmpty().MaximumLength(Company.TitleMaxLength);
        v.RuleFor(c => c.ShortName).NotEmpty().MaximumLength(Company.ShortNameMaxLength);
        v.RuleFor(c => c.TaxOffice).MaximumLength(Company.TaxOfficeMaxLength);
        v.RuleFor(c => c.TaxNumber).Cascade(CascadeMode.Stop).NotEmpty().MustBeValid(Vkn.Create);
        v.RuleFor(c => c.MersisNo).MustBeValid(MersisNo.Create).When(c => !string.IsNullOrWhiteSpace(c.MersisNo));
        v.RuleFor(c => c.SgkWorkplaceNo).MustBeValid(SgkWorkplaceNo.Create).When(c => !string.IsNullOrWhiteSpace(c.SgkWorkplaceNo));
        v.RuleFor(c => c.AddressLine1).NotEmpty().MaximumLength(Address.LineMaxLength);
        v.RuleFor(c => c.AddressLine2).MaximumLength(Address.LineMaxLength);
        v.RuleFor(c => c.ProvinceCode).GreaterThan(0);
        v.RuleFor(c => c.DistrictId).GreaterThan(0);
        v.RuleFor(c => c.PostalCode)
            .Matches("^[0-9]{5}$").WithErrorCode(CommonErrors.InvalidPostalCode.Code)
            .When(c => !string.IsNullOrWhiteSpace(c.PostalCode));
        v.RuleFor(c => c.Phone).MustBeValid(PhoneNumber.Create).When(c => !string.IsNullOrWhiteSpace(c.Phone));
        v.RuleFor(c => c.Email).MustBeValid(Email.Create).When(c => !string.IsNullOrWhiteSpace(c.Email));
        v.RuleFor(c => c.DefaultCulture).Cascade(CascadeMode.Stop).NotEmpty().Must(IsCulture).WithErrorCode(CommonErrors.InvalidCulture.Code);
        v.RuleFor(c => c.TimeZoneId).Cascade(CascadeMode.Stop).NotEmpty().Must(IsTimeZone).WithErrorCode(CommonErrors.InvalidTimeZone.Code);
    }

    private static bool IsCulture(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        try
        {
            return !string.IsNullOrEmpty(CultureInfo.GetCultureInfo(name, predefinedOnly: true).Name);
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static bool IsTimeZone(string? id) => !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);
}

internal sealed class CreateCompanyCommandValidator : AbstractValidator<CreateCompanyCommand>
{
    public CreateCompanyCommandValidator()
    {
        RuleFor(c => c.Code)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(Company.CodeMaxLength)
            .Matches("^[A-Za-z0-9][A-Za-z0-9_-]*$").WithErrorCode(CommonErrors.InvalidCode.Code);
        this.ValidateCompanyFields();
    }
}

internal sealed class UpdateCompanyCommandValidator : AbstractValidator<UpdateCompanyCommand>
{
    public UpdateCompanyCommandValidator() => this.ValidateCompanyFields();
}

internal static class CompanyDetailsFactory
{
    /// <summary>Validated input → domain details; the location comes from the reference data.</summary>
    public static CompanyDetails Create(ICompanyFields f, Location location) => new(
        f.Title,
        f.ShortName,
        f.TaxOffice,
        Vkn.Create(f.TaxNumber).Value,
        string.IsNullOrWhiteSpace(f.MersisNo) ? null : MersisNo.Create(f.MersisNo).Value,
        string.IsNullOrWhiteSpace(f.SgkWorkplaceNo) ? null : SgkWorkplaceNo.Create(f.SgkWorkplaceNo).Value,
        location,
        string.IsNullOrWhiteSpace(f.Phone) ? null : PhoneNumber.Create(f.Phone).Value,
        string.IsNullOrWhiteSpace(f.Email) ? null : Email.Create(f.Email).Value,
        CultureInfo.GetCultureInfo(f.DefaultCulture).Name,
        f.TimeZoneId);
}

internal sealed class CreateCompanyCommandHandler(
    IPlatformDbContext db,
    PlatformAccess access,
    LocationResolver locations,
    IIntegrationEventPublisher events) : ICommandHandler<CreateCompanyCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateCompanyCommand request, CancellationToken cancellationToken)
    {
        // A new company is outside every company scope, so only tenant wide managers may create one.
        if (!(await access.GetAsync(request.Permission, cancellationToken)).All)
        {
            return CommonErrors.Forbidden;
        }

        var code = Company.NormalizeCode(request.Code);
        if (await db.Companies.AnyAsync(c => c.Code == code, cancellationToken))
        {
            return PlatformErrors.CompanyCodeTaken;
        }

        var location = await locations.ResolveAsync(
            request.ProvinceCode, request.DistrictId, request.AddressLine1, request.AddressLine2, request.PostalCode, cancellationToken);
        if (location.IsFailure)
        {
            return location.Error;
        }

        var company = Company.Create(code, CompanyDetailsFactory.Create(request, location.Value));
        db.Companies.Add(company);
        await events.PublishAsync(
            new CompanyCreated(company.Code, company.Title, company.ShortName, company.IsActive) { CompanyId = company.Id.Value },
            cancellationToken);
        return company.Id.Value;
    }
}

internal sealed class UpdateCompanyCommandHandler(
    IPlatformDbContext db,
    PlatformAccess access,
    LocationResolver locations,
    IIntegrationEventPublisher events) : ICommandHandler<UpdateCompanyCommand>
{
    public async Task<Result> Handle(UpdateCompanyCommand request, CancellationToken cancellationToken)
    {
        var id = CompanyId.From(request.Id);
        var scope = await access.GetAsync(request.Permission, cancellationToken);
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (company is null || !scope.CoversCompany(company.Id))
        {
            return CommonErrors.NotFound;
        }

        var location = await locations.ResolveAsync(
            request.ProvinceCode, request.DistrictId, request.AddressLine1, request.AddressLine2, request.PostalCode, cancellationToken);
        if (location.IsFailure)
        {
            return location.Error;
        }

        // The client's version decides whether someone else changed the company meanwhile (409 on mismatch).
        db.Entry(company).Property(ShadowProperties.Version).OriginalValue = request.Version;
        company.Update(CompanyDetailsFactory.Create(request, location.Value), request.IsActive);
        await events.PublishAsync(
            new CompanyUpdated(company.Code, company.Title, company.ShortName, company.IsActive) { CompanyId = company.Id.Value },
            cancellationToken);
        return Result.Success();
    }
}
