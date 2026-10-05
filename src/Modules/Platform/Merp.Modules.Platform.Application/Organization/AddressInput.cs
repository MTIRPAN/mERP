using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Organization;

/// <summary>
/// Address entered in a form. Turkish addresses pick the province and district from the reference data
/// (<see cref="ProvinceCode"/>, <see cref="DistrictCode"/>); their names are filled in. Foreign addresses give
/// <see cref="City"/> and <see cref="District"/> as text.
/// </summary>
public sealed record AddressInput(
    string? Line1,
    string? Line2 = null,
    int? ProvinceCode = null,
    int? DistrictCode = null,
    string? City = null,
    string? District = null,
    string? PostalCode = null,
    string? CountryCode = Address.DefaultCountryCode);

/// <summary>Turns an <see cref="AddressInput"/> into an <see cref="Address"/>; field errors are under <c>address.</c>.</summary>
public sealed class AddressResolver(IPlatformDbContext db)
{
    public const string Field = "address";

    public async Task<Result<Address>> ResolveAsync(AddressInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var country = string.IsNullOrWhiteSpace(input.CountryCode) ? Address.DefaultCountryCode : input.CountryCode.Trim().ToUpperInvariant();
        if (!string.Equals(country, Address.DefaultCountryCode, StringComparison.Ordinal))
        {
            return Prefix(Address.Create(input.Line1, input.Line2, input.District, input.City, input.PostalCode, country));
        }

        var errors = new List<FieldError>();
        string? provinceName = null;
        string? districtName = null;
        if (input.ProvinceCode is null)
        {
            errors.Add(new FieldError($"{Field}.provinceCode", CommonErrors.Required.Code));
        }
        else
        {
            provinceName = await db.Provinces.Where(p => p.Id == input.ProvinceCode).Select(p => p.Name).SingleOrDefaultAsync(cancellationToken);
            if (provinceName is null)
            {
                errors.Add(new FieldError($"{Field}.provinceCode", OrganizationErrors.UnknownProvinceCode));
            }
        }

        if (input.DistrictCode is null)
        {
            errors.Add(new FieldError($"{Field}.districtCode", CommonErrors.Required.Code));
        }
        else if (provinceName is not null)
        {
            districtName = await db.Districts
                .Where(d => d.Id == input.DistrictCode && d.ProvinceId == input.ProvinceCode)
                .Select(d => d.Name)
                .SingleOrDefaultAsync(cancellationToken);
            if (districtName is null)
            {
                errors.Add(new FieldError($"{Field}.districtCode", OrganizationErrors.UnknownDistrictCode));
            }
        }

        var address = Prefix(Address.Create(
            input.Line1,
            input.Line2,
            districtName,
            provinceName ?? "-",
            input.PostalCode,
            country,
            provinceName is null ? null : input.ProvinceCode,
            districtName is null ? null : input.DistrictCode));

        if (address.IsFailure)
        {
            errors.AddRange(address.Error.Fields);
        }

        return errors.Count > 0 ? Error.Validation(errors) : address;
    }

    private static Result<Address> Prefix(Result<Address> result) =>
        result.IsSuccess
            ? result
            : Error.Validation(result.Error.Fields.Select(f => f with { Field = $"{Field}.{f.Field}" }));
}
