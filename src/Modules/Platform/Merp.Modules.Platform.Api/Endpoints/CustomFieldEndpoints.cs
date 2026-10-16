using Merp.Application.Abstractions.Messaging;
using Merp.Modules.Platform.Application;
using Merp.Modules.Platform.Application.CustomFields;
using Merp.SharedKernel.Results;
using Merp.Web.Endpoints;
using Merp.Web.Errors;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.Platform.Api.Endpoints;

/// <summary>Custom field definitions (PLAN P6, D12, D30); administration needs the Tenant scope.</summary>
internal static class CustomFieldEndpoints
{
    public static void MapCustomFieldEndpoints(this IEndpointRouteBuilder group)
    {
        var fields = group.MapGroup("/custom-fields");

        fields.MapGet("/entity-types", ListEntityTypesAsync)
            .RequirePermission(PlatformPermissions.CustomFields.Read)
            .WithName("ListCustomFieldEntityTypes")
            .WithSummary("Entity types that can carry custom fields")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        fields.MapGet("/definitions", ListDefinitionsAsync)
            .RequirePermission(PlatformPermissions.CustomFields.Read)
            .WithName("ListCustomFieldDefinitions")
            .WithSummary("Custom field definitions, active or not")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        fields.MapPost("/definitions", CreateAsync)
            .RequirePermission(PlatformPermissions.CustomFields.Manage)
            .WithName("CreateCustomField")
            .WithSummary("Define a custom field")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        fields.MapPut("/definitions/{id:guid}", UpdateAsync)
            .RequirePermission(PlatformPermissions.CustomFields.Manage)
            .WithName("UpdateCustomField")
            .WithSummary("Change a custom field, or switch it off")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Every form with custom fields reads them; the data is not sensitive (D22)
        fields.MapGet("/form", GetFormAsync)
            .RequireAuthenticatedUser()
            .WithName("GetCustomFieldForm")
            .WithSummary("Active custom fields of an entity type for a company, as a form shows them");
    }

    private static async Task<Results<Ok<IReadOnlyList<CustomFieldEntityItem>>, ProblemHttpResult>> ListEntityTypesAsync(ISender sender, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetCustomFieldEntityTypesQuery(), cancellationToken));

    /// <param name="entityType">Only this entity type, e.g. platform.company.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<IReadOnlyList<CustomFieldDefinitionItem>>, ProblemHttpResult>> ListDefinitionsAsync(
        string? entityType,
        ISender sender,
        CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetCustomFieldDefinitionsQuery(entityType), cancellationToken));

    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> CreateAsync(
        CreateCustomFieldCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/platform/custom-fields/definitions/{result.Value}", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UpdateAsync(
        Guid id,
        UpdateCustomFieldCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command with { Id = id }, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
    }

    /// <param name="entityType">Entity type, e.g. platform.company.</param>
    /// <param name="companyId">Company of the record; adds that company's own fields.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<IReadOnlyList<CustomFieldFormItem>>, ProblemHttpResult>> GetFormAsync(
        string entityType,
        Guid? companyId,
        ISender sender,
        CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetCustomFieldFormQuery(entityType, companyId), cancellationToken));

    private static Results<Ok<T>, ProblemHttpResult> Respond<T>(Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
}
