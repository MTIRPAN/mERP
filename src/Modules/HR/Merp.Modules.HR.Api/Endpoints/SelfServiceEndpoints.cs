using Merp.Application.Abstractions.Messaging;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Employees;
using Merp.Modules.HR.Application.SelfService;
using Merp.SharedKernel.Results;
using Merp.Web.Endpoints;
using Merp.Web.Errors;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.HR.Api.Endpoints;

/// <summary>Self service of the person linked to the user (PLAN H3b, D39) and HR's view of the change requests.</summary>
internal static class SelfServiceEndpoints
{
    public static void MapSelfServiceEndpoints(this IEndpointRouteBuilder group)
    {
        var me = group.MapGroup("/me");

        me.MapGet("/", async Task<Results<Ok<EmployeeDetails>, ProblemHttpResult>> (ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetMyEmployeeQuery(), ct)))
            .RequirePermission(HrPermissions.Self.ProfileRead)
            .WithName("GetMyEmployee")
            .WithSummary("Kartım: the card of the person linked to the user; 404 without one")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapGet("/profile-changes", async Task<Results<Ok<IReadOnlyList<ProfileChangeItem>>, ProblemHttpResult>> (ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetMyProfileChangesQuery(), ct)))
            .RequirePermission(HrPermissions.Self.ProfileRequest)
            .WithName("ListMyProfileChanges")
            .WithSummary("The user's change requests, newest first")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapPost("/profile-changes/contacts", (SubmitContactsChangeCommand command, ISender sender, CancellationToken ct) =>
                CreatedAsync(sender.Send(command, ct)))
            .RequirePermission(HrPermissions.Self.ProfileRequest)
            .WithName("SubmitContactsChange")
            .WithSummary("Ask for new e-mails, phones, addresses and emergency contacts; with requestId, resend a returned request")
            .ProducesSubmitProblems();

        me.MapPost("/profile-changes/family", (SubmitFamilyChangeCommand command, ISender sender, CancellationToken ct) =>
                CreatedAsync(sender.Send(command, ct)))
            .RequirePermission(HrPermissions.Self.ProfileRequest)
            .WithName("SubmitFamilyChange")
            .WithSummary("Ask for new family members; with requestId, resend a returned request")
            .ProducesSubmitProblems();

        me.MapPost("/profile-changes/iban", (SubmitIbanChangeCommand command, ISender sender, CancellationToken ct) =>
                CreatedAsync(sender.Send(command, ct)))
            .RequirePermission(HrPermissions.Self.ProfileRequest)
            .WithName("SubmitIbanChange")
            .WithSummary("Ask for a new IBAN; with requestId, resend a returned request")
            .ProducesSubmitProblems();

        me.MapPost("/profile-changes/{id:guid}/withdraw", async Task<Results<NoContent, ProblemHttpResult>> (Guid id, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new WithdrawProfileChangeCommand(id), ct);
                return result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
            })
            .RequirePermission(HrPermissions.Self.ProfileRequest)
            .WithName("WithdrawProfileChange")
            .WithSummary("Withdraw an open change request")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/employees/{id:guid}/profile-changes", async Task<Results<Ok<IReadOnlyList<ProfileChangeItem>>, ProblemHttpResult>> (Guid id, ISender sender, CancellationToken ct) =>
                OkOrProblem(await sender.Send(new GetEmployeeProfileChangesQuery(id), ct)))
            .RequirePermission(HrPermissions.Employees.Read)
            .WithName("ListEmployeeProfileChanges")
            .WithSummary("The change requests of a person in the caller's scope, newest first")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static RouteHandlerBuilder ProducesSubmitProblems(this RouteHandlerBuilder builder) =>
        builder
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static Results<Ok<T>, ProblemHttpResult> OkOrProblem<T>(Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();

    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> CreatedAsync(Task<Result<Guid>> sending)
    {
        var result = await sending;
        return result.IsSuccess
            ? TypedResults.Created("/api/hr/me/profile-changes", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }
}
