using System.Globalization;
using Merp.Application.Abstractions.Messaging;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Locations;

/// <summary>Provinces of Türkiye in Turkish alphabetical order. Public reference data for every signed-in user (D22).</summary>
public sealed record GetProvincesQuery : IQuery<IReadOnlyList<LocationOption>>;

/// <summary>Districts of a province in Turkish alphabetical order; 404 for an unknown province.</summary>
public sealed record GetDistrictsQuery(int ProvinceCode) : IQuery<IReadOnlyList<LocationOption>>;

/// <remarks>Code is the plate number of a province or the NVİ code of a district.</remarks>
public sealed record LocationOption(int Code, string Name);

internal static class LocationOrder
{
    // Names are Turkish proper nouns: Ç, Ğ, İ, Ö, Ş and Ü sort after C, G, I, O, S and U.
    private static readonly StringComparer Turkish = StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: false);

    public static IReadOnlyList<LocationOption> Sort(IEnumerable<LocationOption> options) =>
        [.. options.OrderBy(o => o.Name, Turkish)];
}

internal sealed class GetProvincesQueryHandler(IPlatformDbContext db) : IQueryHandler<GetProvincesQuery, IReadOnlyList<LocationOption>>
{
    public async Task<Result<IReadOnlyList<LocationOption>>> Handle(GetProvincesQuery request, CancellationToken cancellationToken) =>
        Result.Success(LocationOrder.Sort(await db.Provinces.AsNoTracking()
            .Select(p => new LocationOption(p.Id, p.Name))
            .ToListAsync(cancellationToken)));
}

internal sealed class GetDistrictsQueryHandler(IPlatformDbContext db) : IQueryHandler<GetDistrictsQuery, IReadOnlyList<LocationOption>>
{
    public async Task<Result<IReadOnlyList<LocationOption>>> Handle(GetDistrictsQuery request, CancellationToken cancellationToken)
    {
        if (!await db.Provinces.AnyAsync(p => p.Id == request.ProvinceCode, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        return Result.Success(LocationOrder.Sort(await db.Districts.AsNoTracking()
            .Where(d => d.ProvinceId == request.ProvinceCode)
            .Select(d => new LocationOption(d.Id, d.Name))
            .ToListAsync(cancellationToken)));
    }
}
