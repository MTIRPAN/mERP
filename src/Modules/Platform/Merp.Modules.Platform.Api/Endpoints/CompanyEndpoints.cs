using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Companies;
using Merp.Web.Errors;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Platform.Api.Endpoints;

internal static class CompanyEndpoints
{
    private const string Path = "/api/platform/companies";

    public static void MapCompanyEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/companies", ListAsync)
            .RequirePermission(PlatformPermissions.Companies.Read)
            .WithName("ListCompanies")
            .WithSummary("Companies in the caller's scope; sort: code, title, shortName, city, isActive")
            .ProducesValidationProblem();

        group.MapGet("/companies/{id:guid}", GetAsync)
            .RequirePermission(PlatformPermissions.Companies.Read)
            .WithName("GetCompany")
            .WithSummary("A company; 404 when missing or outside the caller's scope");

        group.MapPost("/companies", CreateAsync)
            .RequirePermission(PlatformPermissions.Companies.Manage)
            .WithName("CreateCompany")
            .WithSummary("Creates a company (tenant wide managers only)")
            .ProducesValidationProblem();

        group.MapPut("/companies/{id:guid}", UpdateAsync)
            .RequirePermission(PlatformPermissions.Companies.Manage)
            .WithName("UpdateCompany")
            .WithSummary("Updates a company; 409 when the version is stale")
            .ProducesValidationProblem();
    }

    private static async Task<Results<Ok<PagedResult<CompanyListItem>>, ProblemHttpResult>> ListAsync(
        [AsParameters] ListCompaniesQuery query, ISender sender, CancellationToken cancellationToken) =>
        (await sender.Send(query, cancellationToken)).ToOkOrProblem();

    private static async Task<Results<Ok<CompanyDto>, ProblemHttpResult>> GetAsync(Guid id, ISender sender, CancellationToken cancellationToken) =>
        (await sender.Send(new GetCompanyQuery(id), cancellationToken)).ToOkOrProblem();

    private static async Task<Results<Created<CreatedResponse>, ProblemHttpResult>> CreateAsync(
        CreateCompanyCommand command, ISender sender, CancellationToken cancellationToken) =>
        (await sender.Send(command, cancellationToken)).ToCreatedOrProblem(Path);

    private static async Task<Results<NoContent, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateCompanyRequest request, ISender sender, CancellationToken cancellationToken) =>
        (await sender.Send(request.ToCommand(id), cancellationToken)).ToNoContentOrProblem();
}

/// <summary>Body of <c>PUT /api/platform/companies/{id}</c>; the code cannot change.</summary>
public sealed record UpdateCompanyRequest(
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
    bool IsActive)
{
    public UpdateCompanyCommand ToCommand(Guid id) => new(
        id, Version, Title, ShortName, TaxOffice, TaxNumber, MersisNo, SgkWorkplaceNo, AddressLine1, AddressLine2,
        ProvinceCode, DistrictId, PostalCode, Phone, Email, DefaultCulture, TimeZoneId, IsActive);
}
