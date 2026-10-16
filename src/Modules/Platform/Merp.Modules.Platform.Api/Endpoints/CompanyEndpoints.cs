using Merp.Application.Abstractions.Exports;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Companies;
using Merp.Modules.Platform.Application.CustomFields;
using Merp.Modules.Platform.Application.Exports;
using Merp.Modules.Platform.Domain.Companies;
using Merp.Web.Endpoints;
using Merp.Web.Errors;
using Merp.Web.Files;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Platform.Api.Endpoints;

internal static class CompanyEndpoints
{
    public static void MapCompanyEndpoints(this IEndpointRouteBuilder group)
    {
        var companies = group.MapGroup("/companies");

        companies.MapGet("/", ListAsync)
            .RequirePermission(PlatformPermissions.Companies.Read)
            .WithName("ListCompanies")
            .WithSummary("Companies in the caller's scope")
            .ProducesValidationProblem();

        companies.MapGet("/export", ExportAsync)
            .RequirePermission(PlatformPermissions.Companies.Read)
            .WithName("ExportCompanies")
            .WithSummary("The company list as Excel, with the list's filters and sort and the chosen columns, custom fields included (D35)")
            .ProducesValidationProblem();

        companies.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(PlatformPermissions.Companies.Read)
            .WithName("GetCompany")
            .WithSummary("A company; 404 when it is outside the caller's scope")
            .ProducesProblem(StatusCodes.Status404NotFound);

        companies.MapPost("/", CreateAsync)
            .RequirePermission(PlatformPermissions.Companies.Manage)
            .WithName("CreateCompany")
            .WithSummary("Create a company (Tenant scope required)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        companies.MapPut("/{id:guid}", UpdateAsync)
            .RequirePermission(PlatformPermissions.Companies.Manage)
            .WithName("UpdateCompany")
            .WithSummary("Update a company's details and active flag; the code never changes")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        companies.MapPut("/{id:guid}/logo", SetLogoAsync)
            .RequirePermission(PlatformPermissions.Companies.Manage)
            .WithMetadata(new RequestSizeLimitAttribute(FileTransfer.RequestLimit(SetCompanyLogoCommand.MaxBytes)))
            // Authentication is a bearer token, never a cookie, so cross-site request forgery does not apply.
            .DisableAntiforgery()
            .WithName("SetCompanyLogo")
            .WithSummary("Set or replace the logo: PNG, JPEG or WebP, at most 1 MB; download it from /api/platform/files/{logoFileId}")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        companies.MapDelete("/{id:guid}/logo", RemoveLogoAsync)
            .RequirePermission(PlatformPermissions.Companies.Manage)
            .WithName("RemoveCompanyLogo")
            .WithSummary("Remove the logo")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="sort">code, shortName, title, taxNumber, isActive or createdAt; a leading "-" sorts descending.</param>
    /// <param name="search">Part of the code, short name, title or tax number.</param>
    /// <param name="isActive">Only active or only inactive companies.</param>
    /// <param name="customFields">JSON object of custom field codes and values the companies must have, e.g. {"shoe_size":42}.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<CompanyListItem>>, ProblemHttpResult>> ListAsync(
        int? page,
        int? pageSize,
        string? sort,
        string? search,
        bool? isActive,
        string? customFields,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetCompaniesQuery(new PageRequest(page, pageSize, sort), search, isActive, customFields), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    /// <param name="columns">Column keys, comma separated (code,customFields.region); every column when empty.</param>
    /// <param name="sort">The list's sort.</param>
    /// <param name="search">The list's filter.</param>
    /// <param name="isActive">The list's filter.</param>
    /// <param name="customFields">The list's custom field filter.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="exporter">Writes the file.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> ExportAsync(
        string? columns,
        string? sort,
        string? search,
        bool? isActive,
        string? customFields,
        ISender sender,
        IListExporter exporter,
        CancellationToken cancellationToken)
    {
        var fields = await sender.Send(new GetCustomFieldFormQuery(PlatformCustomFieldEntities.Company, null), cancellationToken);
        if (fields.IsFailure)
        {
            return (ProblemHttpResult)fields.Error.ToProblem();
        }

        var result = await exporter.ExportAsync(
            new ExportList<CompanyListItem>(
                "web.platform.companies.title",
                typeof(Company),
                PlatformExports.Companies(fields.Value),
                paging => new GetCompaniesQuery(paging, search, isActive, customFields),
                sort,
                ExportRules.ParseColumns(columns),
                new Dictionary<string, object?> { ["search"] = search, ["isActive"] = isActive, ["customFields"] = customFields }),
            cancellationToken);
        return result.ToFile();
    }

    private static async Task<Results<Ok<CompanyDetails>, ProblemHttpResult>> GetAsync(Guid id, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetCompanyQuery(id), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> CreateAsync(
        CreateCompanyCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/platform/companies/{result.Value}", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UpdateAsync(
        Guid id,
        UpdateCompanyCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command with { Id = id }, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
    }

    /// <param name="id">The company.</param>
    /// <param name="file">The logo.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<EntityCreatedResponse>, ProblemHttpResult>> SetLogoAsync(
        Guid id,
        IFormFile? file,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new SetCompanyLogoCommand(id, file.ToUpload()), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(new EntityCreatedResponse(result.Value)) : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveLogoAsync(Guid id, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RemoveCompanyLogoCommand(id), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
    }
}
