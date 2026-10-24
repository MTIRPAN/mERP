using Merp.Application.Abstractions.Messaging;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Leave;
using Merp.Web.Errors;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.HR.Api.Endpoints;

/// <summary>Leave balances of an employment and HR's corrections (PLAN L3, D43).</summary>
internal static class LeaveBalanceEndpoints
{
    public static void MapLeaveBalanceEndpoints(this IEndpointRouteBuilder group)
    {
        var balances = group.MapGroup("/employees/{employeeId:guid}/employments/{employmentId:guid}/leave-balances");

        balances.MapGet("/", ListAsync)
            .RequirePermission(HrPermissions.Leave.Balances.Read)
            .WithName("ListLeaveBalances")
            .WithSummary("An employment's leave balances: earned, used, pending, available, the next entitlement and the movements")
            .ProducesProblem(StatusCodes.Status404NotFound);

        balances.MapPost("/adjustments", AdjustAsync)
            .RequirePermission(HrPermissions.Leave.Balances.Manage)
            .WithName("AdjustLeaveBalance")
            .WithSummary("Correct a leave balance in or out; the reason is required")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<IReadOnlyList<LeaveBalanceItem>>, ProblemHttpResult>> ListAsync(
        Guid employeeId,
        Guid employmentId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetLeaveBalancesQuery(employeeId, employmentId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> AdjustAsync(
        Guid employeeId,
        Guid employmentId,
        AdjustLeaveBalanceCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command with { EmployeeId = employeeId, EmploymentId = employmentId }, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
    }
}
