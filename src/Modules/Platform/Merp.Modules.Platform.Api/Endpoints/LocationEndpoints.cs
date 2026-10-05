using Merp.Application.Abstractions.Messaging;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Locations;
using Merp.Web.Errors;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Platform.Api.Endpoints;

internal static class LocationEndpoints
{
    public static void MapLocationEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/provinces", ListProvincesAsync)
            .RequirePermission(PlatformPermissions.ReferenceData.Read)
            .WithName("ListProvinces")
            .WithSummary("Turkey's 81 provinces by licence plate code");

        group.MapGet("/provinces/{code:int}/districts", ListDistrictsAsync)
            .RequirePermission(PlatformPermissions.ReferenceData.Read)
            .WithName("ListDistricts")
            .WithSummary("Districts of a province in Turkish alphabetical order");
    }

    private static async Task<Results<Ok<IReadOnlyList<ProvinceDto>>, ProblemHttpResult>> ListProvincesAsync(ISender sender, CancellationToken cancellationToken) =>
        (await sender.Send(new ListProvincesQuery(), cancellationToken)).ToOkOrProblem();

    private static async Task<Results<Ok<IReadOnlyList<DistrictDto>>, ProblemHttpResult>> ListDistrictsAsync(int code, ISender sender, CancellationToken cancellationToken) =>
        (await sender.Send(new ListDistrictsQuery(code), cancellationToken)).ToOkOrProblem();
}
