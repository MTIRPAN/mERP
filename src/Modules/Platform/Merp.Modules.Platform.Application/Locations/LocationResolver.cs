using Merp.Modules.Platform.Application.Common;
using Merp.Modules.Platform.Domain.Companies;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Locations;

/// <summary>Turns province and district ids of a form into a <see cref="Location"/> with the official names.</summary>
internal sealed class LocationResolver(IPlatformDbContext db)
{
    public async Task<Result<Location>> ResolveAsync(
        int provinceCode,
        int districtId,
        string addressLine1,
        string? addressLine2,
        string? postalCode,
        CancellationToken cancellationToken)
    {
        var province = await db.Provinces.AsNoTracking().FirstOrDefaultAsync(p => p.Code == provinceCode, cancellationToken);
        if (province is null)
        {
            return PlatformErrors.UnknownProvince;
        }

        var district = await db.Districts.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == districtId && d.ProvinceCode == provinceCode, cancellationToken);
        if (district is null)
        {
            return PlatformErrors.UnknownDistrict;
        }

        var address = Address.Create(addressLine1, addressLine2, district.Name, province.Name, postalCode);
        return address.IsSuccess ? new Location(address.Value, province.Code, district.Id) : address.Error;
    }
}
