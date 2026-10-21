using Merp.Application.Abstractions.Messaging;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Documents;
using Merp.SharedKernel.Results;
using Merp.Web.Endpoints;
using Merp.Web.Errors;
using Merp.Web.Files;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.HR.Api.Endpoints;

/// <summary>Employee documents and the document types (PLAN H2b2, D38).</summary>
internal static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this IEndpointRouteBuilder group)
    {
        var documents = group.MapGroup("/employees/{employeeId:guid}/documents");

        documents.MapGet("/", ListAsync)
            .RequirePermission(HrPermissions.Employees.Documents.Read)
            .WithName("ListEmployeeDocuments")
            .WithSummary("A person's documents, whether the caller may change them, and which files are accepted")
            .ProducesProblem(StatusCodes.Status404NotFound);

        documents.MapPost("/", AddAsync)
            .RequirePermission(HrPermissions.Employees.Documents.Manage)
            .WithMetadata(new RequestSizeLimitAttribute(FileTransfer.AttachmentRequestLimit(group)))
            // Authentication is a bearer token, never a cookie, so cross-site request forgery does not apply.
            .DisableAntiforgery()
            .WithName("AddEmployeeDocument")
            .WithSummary("Add a document with its file; Files:AllowedExtensions and Files:MaxBytes apply")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        documents.MapPut("/{documentId:guid}", UpdateAsync)
            .RequirePermission(HrPermissions.Employees.Documents.Manage)
            .WithName("UpdateEmployeeDocument")
            .WithSummary("Change a document's type, number, validity or note; a new validity date restarts its reminders")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        documents.MapPut("/{documentId:guid}/file", ReplaceFileAsync)
            .RequirePermission(HrPermissions.Employees.Documents.Manage)
            .WithMetadata(new RequestSizeLimitAttribute(FileTransfer.AttachmentRequestLimit(group)))
            // Authentication is a bearer token, never a cookie, so cross-site request forgery does not apply.
            .DisableAntiforgery()
            .WithName("ReplaceEmployeeDocumentFile")
            .WithSummary("Replace a document's file")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        documents.MapDelete("/{documentId:guid}", DeleteAsync)
            .RequirePermission(HrPermissions.Employees.Documents.Manage)
            .WithName("DeleteEmployeeDocument")
            .WithSummary("Delete a document and its file")
            .ProducesProblem(StatusCodes.Status404NotFound);

        documents.MapGet("/{documentId:guid}/file", DownloadAsync)
            .RequirePermission(HrPermissions.Employees.Documents.Read)
            .WithName("DownloadEmployeeDocument")
            .WithSummary("A document's file; 404 unless the caller sees the person's documents")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);

        var types = group.MapGroup("/document-types");

        types.MapGet("/", ListTypesAsync)
            .RequirePermission(HrPermissions.Employees.Documents.Read)
            .WithName("ListEmployeeDocumentTypes")
            .WithSummary("The document types of the tenant, active ones unless asked");

        types.MapPost("/", CreateTypeAsync)
            .RequirePermission(HrPermissions.Employees.Documents.Manage)
            .WithName("CreateEmployeeDocumentType")
            .WithSummary("Add a document type (Tenant scope required)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        types.MapPut("/{id:guid}", (Guid id, UpdateDocumentTypeCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(HrPermissions.Employees.Documents.Manage)
            .WithName("UpdateEmployeeDocumentType")
            .WithSummary("Rename a document type or retire it; the code never changes (Tenant scope required)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> NoContentAsync(Task<Result> sending)
    {
        var result = await sending;
        return result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<Ok<EmployeeDocumentList>, ProblemHttpResult>> ListAsync(Guid employeeId, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetEmployeeDocumentsQuery(employeeId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    /// <param name="employeeId">The person.</param>
    /// <param name="typeId">An active document type.</param>
    /// <param name="number">The document's number, if it has one.</param>
    /// <param name="issuedOn">Day of issue.</param>
    /// <param name="validUntil">Last day of validity; HR is reminded 30 and 7 days before.</param>
    /// <param name="note">Free text.</param>
    /// <param name="file">The scan or the file.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> AddAsync(
        Guid employeeId,
        [FromForm] Guid? typeId,
        [FromForm] string? number,
        [FromForm] DateOnly? issuedOn,
        [FromForm] DateOnly? validUntil,
        [FromForm] string? note,
        IFormFile? file,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new AddEmployeeDocumentCommand(employeeId, typeId, number, issuedOn, validUntil, note, file.ToUpload()), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/hr/employees/{employeeId}/documents/{result.Value}/file", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static Task<Results<NoContent, ProblemHttpResult>> UpdateAsync(
        Guid employeeId,
        Guid documentId,
        UpdateEmployeeDocumentCommand command,
        ISender sender,
        CancellationToken cancellationToken) =>
        NoContentAsync(sender.Send(command with { EmployeeId = employeeId, DocumentId = documentId }, cancellationToken));

    /// <param name="employeeId">The person.</param>
    /// <param name="documentId">The document.</param>
    /// <param name="file">The new file.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static Task<Results<NoContent, ProblemHttpResult>> ReplaceFileAsync(
        Guid employeeId,
        Guid documentId,
        IFormFile? file,
        ISender sender,
        CancellationToken cancellationToken) =>
        NoContentAsync(sender.Send(new ReplaceEmployeeDocumentFileCommand(employeeId, documentId, file.ToUpload()), cancellationToken));

    private static Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(Guid employeeId, Guid documentId, ISender sender, CancellationToken cancellationToken) =>
        NoContentAsync(sender.Send(new DeleteEmployeeDocumentCommand(employeeId, documentId), cancellationToken));

    private static async Task<IResult> DownloadAsync(Guid employeeId, Guid documentId, HttpContext context, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetEmployeeDocumentFileQuery(employeeId, documentId), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        var file = result.Value;
        return FileTransfer.Content(context, file.FileName, file.ContentType, file.Length, file.Content);
    }

    /// <param name="includeInactive">Also the retired types.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<IReadOnlyList<EmployeeDocumentTypeItem>>, ProblemHttpResult>> ListTypesAsync(
        bool? includeInactive,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetEmployeeDocumentTypesQuery(includeInactive ?? false), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> CreateTypeAsync(
        CreateDocumentTypeCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/hr/document-types/{result.Value}", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }
}
