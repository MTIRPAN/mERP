using Merp.Application.Abstractions.Messaging;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Numbering;
using Merp.SharedKernel.Results;
using Merp.Web.Errors;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Platform.Api.Endpoints;

/// <summary>Number patterns of the document types (PLAN P5, D29); tenant level, the Tenant scope is required.</summary>
internal static class NumberingEndpoints
{
    public static void MapNumberingEndpoints(this IEndpointRouteBuilder group)
    {
        var sequences = group.MapGroup("/numbering/sequences");

        sequences.MapGet("/", ListAsync)
            .RequirePermission(PlatformPermissions.Numbering.Read)
            .WithName("ListNumberSequences")
            .WithSummary("Number patterns of all document types, defaults and companies' own")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        sequences.MapPost("/", AddAsync)
            .RequirePermission(PlatformPermissions.Numbering.Manage)
            .WithName("AddCompanyNumberSequence")
            .WithSummary("Give a company its own pattern for a document type")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        sequences.MapPut("/{id:guid}", UpdateAsync)
            .RequirePermission(PlatformPermissions.Numbering.Manage)
            .WithName("UpdateNumberSequence")
            .WithSummary("Change a pattern and its reset period")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        sequences.MapDelete("/{id:guid}", DeleteAsync)
            .RequirePermission(PlatformPermissions.Numbering.Manage)
            .WithName("DeleteNumberSequence")
            .WithSummary("Remove a company's own pattern")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Results<Ok<IReadOnlyList<NumberSequenceItem>>, ProblemHttpResult>> ListAsync(ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetNumberSequencesQuery(), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> AddAsync(
        AddCompanyNumberSequenceCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/platform/numbering/sequences/{result.Value}", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UpdateAsync(
        Guid id,
        UpdateNumberSequenceCommand command,
        ISender sender,
        CancellationToken cancellationToken) =>
        NoContent(await sender.Send(command with { Id = id }, cancellationToken));

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(Guid id, ISender sender, CancellationToken cancellationToken) =>
        NoContent(await sender.Send(new DeleteNumberSequenceCommand(id), cancellationToken));

    private static Results<NoContent, ProblemHttpResult> NoContent(Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
}
