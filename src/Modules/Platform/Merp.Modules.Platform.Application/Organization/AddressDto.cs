using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Platform.Application.Organization;

/// <remarks>For Turkish addresses City and District are the province and district names.</remarks>
public sealed record AddressDto(
    string Line1,
    string? Line2,
    int? ProvinceCode,
    int? DistrictCode,
    string City,
    string? District,
    string? PostalCode,
    string CountryCode)
{
    public static AddressDto? From(Address? address) =>
        address is null
            ? null
            : new AddressDto(address.Line1, address.Line2, address.ProvinceCode, address.DistrictCode, address.City, address.District, address.PostalCode, address.CountryCode);
}
