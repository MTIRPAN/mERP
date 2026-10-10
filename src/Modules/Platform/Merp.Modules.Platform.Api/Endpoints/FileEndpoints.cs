using Merp.Application.Abstractions.Messaging;
using Merp.Modules.Platform.Application.Files;
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
/// Attachments and file downloads of Platform records (PLAN P7, D31). A signed-in user is enough at the endpoint;
/// the record's own permissions and data scope decide through its <c>IFileAccessPolicy</c> (D22): no access is 404,
/// read only access cannot upload or delete (403).
/// </summary>
internal static class FileEndpoints
{
    public static void MapFileEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/attachments", ListAsync)
            .RequireAuthenticatedUser()
            .WithName("ListAttachments")
            .WithSummary("Attachments of a record, newest first, and whether the caller may change them")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/attachments", UploadAsync)
            .RequireAuthenticatedUser()
            .WithMetadata(new RequestSizeLimitAttribute(FileTransfer.AttachmentRequestLimit(group)))
            // Authentication is a bearer token, never a cookie, so cross-site request forgery does not apply.
            .DisableAntiforgery()
            .WithName("UploadAttachment")
            .WithSummary("Attach a file to a record; size, extension and content are checked")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/attachments/{id:guid}", DeleteAsync)
            .RequireAuthenticatedUser()
            .WithName("DeleteAttachment")
            .WithSummary("Delete an attachment")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/files/{id:guid}", DownloadAsync)
            .RequireAuthenticatedUser()
            .WithName("DownloadFile")
            .WithSummary("Content of a file (attachment or logo); 404 unless the caller sees its record")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    /// <param name="ownerType">Record type, e.g. platform.company.</param>
    /// <param name="ownerId">The record.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<AttachmentList>, ProblemHttpResult>> ListAsync(
        string? ownerType,
        Guid ownerId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetAttachmentsQuery(ownerType, ownerId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    /// <param name="ownerType">Record type, e.g. platform.company.</param>
    /// <param name="ownerId">The record.</param>
    /// <param name="file">The file; Files:AllowedExtensions and Files:MaxBytes apply.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> UploadAsync(
        [FromForm] string? ownerType,
        [FromForm] Guid ownerId,
        IFormFile? file,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new UploadAttachmentCommand(ownerType, ownerId, file.ToUpload()), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/platform/files/{result.Value}", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(Guid id, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteAttachmentCommand(id), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<IResult> DownloadAsync(Guid id, HttpContext context, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetFileQuery(id), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        var file = result.Value;
        return FileTransfer.Content(context, file.FileName, file.ContentType, file.Length, file.Content);
    }
}
