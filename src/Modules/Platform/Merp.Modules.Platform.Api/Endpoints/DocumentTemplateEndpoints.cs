using Merp.Application.Abstractions.Messaging;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Documents;
using Merp.SharedKernel.Results;
using Merp.Web.Endpoints;
using Merp.Web.Errors;
using Merp.Web.RateLimiting;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Platform.Api.Endpoints;

/// <summary>Document templates and previews (PLAN P12, D36); all need the Tenant scope.</summary>
internal static class DocumentTemplateEndpoints
{
    private const string PdfContentType = "application/pdf";

    public static void MapDocumentTemplateEndpoints(this IEndpointRouteBuilder group)
    {
        var templates = group.MapGroup("/document-templates");

        templates.MapGet("/types", ListTypesAsync)
            .RequirePermission(PlatformPermissions.DocumentTemplates.Read)
            .WithName("ListDocumentTypes")
            .WithSummary("Document types of all modules with their built-in template, sample model and fields")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        templates.MapGet("/", ListAsync)
            .RequirePermission(PlatformPermissions.DocumentTemplates.Read)
            .WithName("ListDocumentTemplates")
            .WithSummary("The tenant's document templates, optionally of one type")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        templates.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(PlatformPermissions.DocumentTemplates.Read)
            .WithName("GetDocumentTemplate")
            .WithSummary("A document template with its content")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        templates.MapPost("/", CreateAsync)
            .RequirePermission(PlatformPermissions.DocumentTemplates.Manage)
            .WithName("CreateDocumentTemplate")
            .WithSummary("Add a template for a document type, a company (or all) and a culture")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        templates.MapPut("/{id:guid}", UpdateAsync)
            .RequirePermission(PlatformPermissions.DocumentTemplates.Manage)
            .WithName("UpdateDocumentTemplate")
            .WithSummary("Change a template's content or turn it off")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        templates.MapDelete("/{id:guid}", DeleteAsync)
            .RequirePermission(PlatformPermissions.DocumentTemplates.Manage)
            .WithName("DeleteDocumentTemplate")
            .WithSummary("Remove a template; the more general one or the built-in one applies again")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        templates.MapPost("/preview", PreviewAsync)
            .RequirePermission(PlatformPermissions.DocumentTemplates.Read)
            .WithName("PreviewDocument")
            .WithSummary("The type's sample model rendered as HTML, without saving")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        templates.MapPost("/draft", DraftAsync)
            .RequirePermission(PlatformPermissions.DocumentTemplates.Manage)
            .RequireRateLimiting(RateLimitingExtensions.AiPolicy)
            .WithName("DraftDocumentTemplate")
            .WithSummary("Draft a template with AI from a description, optionally changing the current one (A10, D63)")
            .WithDescription("Nothing is saved: the draft fills the editor. It is checked against the type's sample model and asked for once more when it does not render.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        templates.MapPost("/preview/pdf", PreviewPdfAsync)
            .RequirePermission(PlatformPermissions.DocumentTemplates.Read)
            .WithName("PreviewDocumentPdf")
            .WithSummary("The type's sample model as PDF, without saving")
            .Produces(StatusCodes.Status200OK, contentType: PdfContentType)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<Results<Ok<IReadOnlyList<DocumentTypeItem>>, ProblemHttpResult>> ListTypesAsync(ISender sender, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetDocumentTypesQuery(), cancellationToken));

    /// <param name="documentType">Only templates of this type, e.g. platform.letter.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<IReadOnlyList<DocumentTemplateItem>>, ProblemHttpResult>> ListAsync(
        string? documentType,
        ISender sender,
        CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetDocumentTemplatesQuery(documentType), cancellationToken));

    private static async Task<Results<Ok<DocumentTemplateDetails>, ProblemHttpResult>> GetAsync(Guid id, ISender sender, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetDocumentTemplateQuery(id), cancellationToken));

    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> CreateAsync(
        CreateDocumentTemplateCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/platform/document-templates/{result.Value}", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UpdateAsync(
        Guid id,
        UpdateDocumentTemplateCommand command,
        ISender sender,
        CancellationToken cancellationToken) =>
        NoContentOrProblem(await sender.Send(command with { Id = id }, cancellationToken));

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(Guid id, ISender sender, CancellationToken cancellationToken) =>
        NoContentOrProblem(await sender.Send(new DeleteDocumentTemplateCommand(id), cancellationToken));

    private static async Task<Results<Ok<DocumentPreview>, ProblemHttpResult>> PreviewAsync(PreviewDocumentQuery query, ISender sender, CancellationToken cancellationToken) =>
        Respond(await sender.Send(query, cancellationToken));

    private static async Task<Results<Ok<DocumentTemplateDraft>, ProblemHttpResult>> DraftAsync(DraftDocumentTemplateQuery query, ISender sender, CancellationToken cancellationToken) =>
        Respond(await sender.Send(query, cancellationToken));

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> PreviewPdfAsync(PreviewDocumentPdfQuery query, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(query, cancellationToken);
        return result.IsSuccess
            ? TypedResults.File(result.Value, PdfContentType, "preview.pdf")
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static Results<Ok<T>, ProblemHttpResult> Respond<T>(Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();

    private static Results<NoContent, ProblemHttpResult> NoContentOrProblem(Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
}
