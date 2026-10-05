using System.Globalization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Locations;

public sealed record ListProvincesQuery : IQuery<IReadOnlyList<ProvinceDto>>, IRequirePermission
{
    public string Permission => PlatformPermissions.ReferenceData.Read;
}

public sealed record ListDistrictsQuery(int ProvinceCode) : IQuery<IReadOnlyList<DistrictDto>>, IRequirePermission
{
    public string Permission => PlatformPermissions.ReferenceData.Read;
}

/// <param name="Code">Licence plate code, 1–81.</param>
/// <param name="Name">Official name.</param>
public sealed record ProvinceDto(int Code, string Name);

/// <param name="Id">Stable district id.</param>
/// <param name="Name">Official name.</param>
public sealed record DistrictDto(int Id, string Name);

internal sealed class ListProvincesQueryHandler(IPlatformDbContext db) : IQueryHandler<ListProvincesQuery, IReadOnlyList<ProvinceDto>>
{
    public async Task<Result<IReadOnlyList<ProvinceDto>>> Handle(ListProvincesQuery request, CancellationToken cancellationToken)
    {
        var provinces = await db.Provinces.AsNoTracking().OrderBy(p => p.Code).Select(p => new ProvinceDto(p.Code, p.Name)).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<ProvinceDto>>(provinces);
    }
}

internal sealed class ListDistrictsQueryHandler(IPlatformDbContext db) : IQueryHandler<ListDistrictsQuery, IReadOnlyList<DistrictDto>>
{
    private static readonly StringComparer TurkishOrder = StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: false);

    public async Task<Result<IReadOnlyList<DistrictDto>>> Handle(ListDistrictsQuery request, CancellationToken cancellationToken)
    {
        if (!await db.Provinces.AnyAsync(p => p.Code == request.ProvinceCode, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        var districts = await db.Districts.AsNoTracking()
            .Where(d => d.ProvinceCode == request.ProvinceCode)
            .Select(d => new DistrictDto(d.Id, d.Name))
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<DistrictDto>>([.. districts.OrderBy(d => d.Name, TurkishOrder)]);
    }
}
