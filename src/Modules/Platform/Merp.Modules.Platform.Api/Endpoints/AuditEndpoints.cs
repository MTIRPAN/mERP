using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.Audit;
using Merp.SharedKernel.Results;
using Merp.Web.Errors;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Platform.Api.Endpoints;

/// <summary>The audit trail of all modules (PLAN P3, D13); the Tenant scope is required (D27).</summary>
internal static class AuditEndpoints
{
    public static void MapAuditEndpoints(this IEndpointRouteBuilder group)
    {
        var audit = group.MapGroup("/audit");

        audit.MapGet("/", SearchAsync)
            .RequirePermission(PlatformPermissions.Audit.Read)
            .WithName("SearchAudit")
            .WithSummary("Field level changes and sensitive reads, newest first")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        audit.MapGet("/entity-types", GetEntityTypesAsync)
            .RequirePermission(PlatformPermissions.Audit.Read)
            .WithName("ListAuditEntityTypes")
            .WithSummary("Audited entity types per module")
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    /// <param name="page">1-based page number; a page may lie at most 10 000 entries deep.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="module">Module (schema), e.g. platform.</param>
    /// <param name="entityType">Entity type name, e.g. Company.</param>
    /// <param name="entityId">Id of the record; with entityType it gives the history of one record.</param>
    /// <param name="userId">Platform user who made the change.</param>
    /// <param name="from">Start of the period (inclusive).</param>
    /// <param name="to">End of the period (inclusive).</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<AuditItem>>, ProblemHttpResult>> SearchAsync(
        int? page,
        int? pageSize,
        string? module,
        string? entityType,
        string? entityId,
        Guid? userId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        ISender sender,
        CancellationToken cancellationToken) =>
        Respond(await sender.Send(
            new SearchAuditQuery(new PageRequest(page, pageSize), module, entityType, entityId, userId, from, to),
            cancellationToken));

    private static async Task<Results<Ok<IReadOnlyList<AuditEntityType>>, ProblemHttpResult>> GetEntityTypesAsync(
        ISender sender,
        CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetAuditEntityTypesQuery(), cancellationToken));

    private static Results<Ok<T>, ProblemHttpResult> Respond<T>(Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
}
