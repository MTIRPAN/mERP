using Merp.Application.Abstractions.Messaging;
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
    /// <summary>
    /// Provinces and districts of Türkiye for address forms of every module. Public reference data: a signed-in
    /// user is enough (D22).
    /// </summary>
    public static void MapLocationEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/locations/provinces", GetProvincesAsync)
            .RequireAuthenticatedUser()
            .WithName("ListProvinces")
            .WithSummary("Provinces (il) of Türkiye in Turkish alphabetical order; the code is the plate number");

        group.MapGet("/locations/provinces/{provinceCode:int}/districts", GetDistrictsAsync)
            .RequireAuthenticatedUser()
            .WithName("ListDistricts")
            .WithSummary("Districts (ilçe) of a province in Turkish alphabetical order")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Ok<IReadOnlyList<LocationOption>>> GetProvincesAsync(ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok((await sender.Send(new GetProvincesQuery(), cancellationToken)).Value);

    private static async Task<Results<Ok<IReadOnlyList<LocationOption>>, ProblemHttpResult>> GetDistrictsAsync(
        int provinceCode,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetDistrictsQuery(provinceCode), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }
}
