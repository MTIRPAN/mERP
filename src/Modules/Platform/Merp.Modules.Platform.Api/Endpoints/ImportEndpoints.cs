using Merp.Application.Abstractions.Messaging;
using Merp.Modules.Platform.Application.Imports;
using Merp.SharedKernel.Results;
using Merp.Web.Errors;
using Merp.Web.Files;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Platform.Api.Endpoints;

/// <summary>
/// Excel imports of all modules (PLAN P11b, D35). A signed-in user is enough for the endpoints (D22): each import
/// needs its definition's permission, and a user sees only their own import jobs (404 otherwise).
/// </summary>
internal static class ImportEndpoints
{
    public static void MapImportEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/imports", ListAsync)
            .RequireAuthenticatedUser()
            .WithName("ListImports")
            .WithSummary("Imports the caller may use");

        var imports = group.MapGroup("/imports");

        imports.MapGet("/{key}/template", TemplateAsync)
            .RequireAuthenticatedUser()
            .WithName("GetImportTemplate")
            .WithSummary("The Excel template of an import, with explanations and lists")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        imports.MapPost("/{key}", StartAsync)
            .RequireAuthenticatedUser()
            .WithMetadata(new RequestSizeLimitAttribute(FileTransfer.RequestLimit(new ImportSettings().MaxBytes)))
            // Authentication is a bearer token, never a cookie, so cross-site request forgery does not apply.
            .DisableAntiforgery()
            .WithName("StartImport")
            .WithSummary("Upload a filled template; it is validated at once, or in the background when large")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        imports.MapGet("/jobs", ListJobsAsync)
            .RequireAuthenticatedUser()
            .WithName("ListImportJobs")
            .WithSummary("The caller's latest import jobs");

        imports.MapGet("/jobs/{id:guid}", GetJobAsync)
            .RequireAuthenticatedUser()
            .WithName("GetImportJob")
            .WithSummary("An import job with its state, counts and first errors")
            .ProducesProblem(StatusCodes.Status404NotFound);

        imports.MapGet("/jobs/{id:guid}/report", ReportAsync)
            .RequireAuthenticatedUser()
            .WithName("GetImportReport")
            .WithSummary("The uploaded rows with each error on its cell")
            .ProducesProblem(StatusCodes.Status404NotFound);

        imports.MapPost("/jobs/{id:guid}/commit", CommitAsync)
            .RequireAuthenticatedUser()
            .WithName("CommitImport")
            .WithSummary("Write a validated file without errors")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Results<Ok<IReadOnlyList<ImportDefinitionItem>>, ProblemHttpResult>> ListAsync(ISender sender, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetImportDefinitionsQuery(), cancellationToken));

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> TemplateAsync(string key, ISender sender, CancellationToken cancellationToken) =>
        File(await sender.Send(new GetImportTemplateQuery(key), cancellationToken));

    /// <param name="key">The import, e.g. platform.branches.</param>
    /// <param name="file">The filled template (xlsx).</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> StartAsync(
        string key,
        IFormFile? file,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new StartImportCommand(key, file.ToUpload()), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/platform/imports/jobs/{result.Value}", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    /// <param name="key">Only jobs of this import.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<IReadOnlyList<ImportJobDetails>>, ProblemHttpResult>> ListJobsAsync(
        string? key,
        ISender sender,
        CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetImportJobsQuery(key), cancellationToken));

    private static async Task<Results<Ok<ImportJobDetails>, ProblemHttpResult>> GetJobAsync(Guid id, ISender sender, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetImportJobQuery(id), cancellationToken));

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> ReportAsync(Guid id, ISender sender, CancellationToken cancellationToken) =>
        File(await sender.Send(new GetImportReportQuery(id), cancellationToken));

    private static async Task<Results<NoContent, ProblemHttpResult>> CommitAsync(Guid id, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CommitImportCommand(id), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static Results<FileContentHttpResult, ProblemHttpResult> File(Result<ImportFile> result) =>
        result.IsSuccess
            ? TypedResults.File(result.Value.Content, ImportFile.ContentType, result.Value.FileName)
            : (ProblemHttpResult)result.Error.ToProblem();

    private static Results<Ok<T>, ProblemHttpResult> Respond<T>(Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
}
