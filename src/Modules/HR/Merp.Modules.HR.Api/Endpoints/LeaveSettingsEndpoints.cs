using Merp.Application.Abstractions.Messaging;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Leave;
using Merp.SharedKernel.Results;
using Merp.Web.Endpoints;
using Merp.Web.Errors;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.HR.Api.Endpoints;

/// <summary>Leave types, leave policies, their assignment rules, per employee overrides (PLAN L1, D41) and leave durations (PLAN L2, D42).</summary>
internal static class LeaveSettingsEndpoints
{
    public static void MapLeaveSettingsEndpoints(this IEndpointRouteBuilder group)
    {
        var leave = group.MapGroup("/leave");

        leave.MapGet("/types", (Guid? companyId, bool? includeInactive, ISender sender, CancellationToken ct) =>
                OkAsync(sender.Send(new GetLeaveTypesQuery(companyId, includeInactive ?? false), ct)))
            .RequirePermission(HrPermissions.Leave.Settings.Read)
            .WithName("ListLeaveTypes")
            .WithSummary("Leave types of every company and of a company, with their versions and legal minimum warnings")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        leave.MapPost("/types", (CreateLeaveTypeCommand command, ISender sender, CancellationToken ct) =>
                CreatedAsync(sender.Send(command, ct), "/api/hr/leave/types"))
            .RequirePermission(HrPermissions.Leave.Settings.Manage)
            .WithName("CreateLeaveType")
            .WithSummary("Add a leave type, of every company (Tenant scope) or of a company")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        leave.MapPut("/types/{id:guid}", (Guid id, UpdateLeaveTypeCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(HrPermissions.Leave.Settings.Manage)
            .WithName("UpdateLeaveType")
            .WithSummary("Change a leave type; versions that have started stay as they are")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        leave.MapGet("/policies", (Guid? companyId, bool? includeInactive, ISender sender, CancellationToken ct) =>
                OkAsync(sender.Send(new GetLeavePoliciesQuery(companyId, includeInactive ?? false), ct)))
            .RequirePermission(HrPermissions.Leave.Settings.Read)
            .WithName("ListLeavePolicies")
            .WithSummary("Leave policies of every company and of a company, with their versions and legal minimum warnings")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        leave.MapPost("/policies", (CreateLeavePolicyCommand command, ISender sender, CancellationToken ct) =>
                CreatedAsync(sender.Send(command, ct), "/api/hr/leave/policies"))
            .RequirePermission(HrPermissions.Leave.Settings.Manage)
            .WithName("CreateLeavePolicy")
            .WithSummary("Add a leave policy, of every company (Tenant scope) or of a company")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        leave.MapPut("/policies/{id:guid}", (Guid id, UpdateLeavePolicyCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { Id = id }, ct)))
            .RequirePermission(HrPermissions.Leave.Settings.Manage)
            .WithName("UpdateLeavePolicy")
            .WithSummary("Change a leave policy; versions that have started stay as they are")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        leave.MapPost("/policies/legal-check", (CheckLeavePolicyRulesQuery query, ISender sender, CancellationToken ct) =>
                OkAsync(sender.Send(query, ct)))
            .RequirePermission(HrPermissions.Leave.Settings.Read)
            .WithName("CheckLeavePolicyRules")
            .WithSummary("The settings of annual leave rules below the law, while a policy is edited")
            .ProducesValidationProblem();

        leave.MapGet("/policy-rules/{companyId:guid}", (Guid companyId, ISender sender, CancellationToken ct) =>
                OkAsync(sender.Send(new GetLeavePolicyRulesQuery(companyId), ct)))
            .RequirePermission(HrPermissions.Leave.Settings.Read)
            .WithName("GetLeavePolicyRules")
            .WithSummary("The policy assignment rules of a company in priority order")
            .ProducesProblem(StatusCodes.Status404NotFound);

        leave.MapPut("/policy-rules/{companyId:guid}", (Guid companyId, ReplaceLeavePolicyRulesCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { CompanyId = companyId }, ct)))
            .RequirePermission(HrPermissions.Leave.Settings.Manage)
            .WithName("ReplaceLeavePolicyRules")
            .WithSummary("Replace the policy assignment rules of a company; their order is their priority")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        leave.MapGet("/policy-resolution", (Guid employmentId, DateOnly? date, ISender sender, CancellationToken ct) =>
                OkAsync(sender.Send(new ResolveLeavePolicyQuery(employmentId, date), ct)))
            .RequirePermission(HrPermissions.Leave.Settings.Read)
            .WithName("ResolveLeavePolicy")
            .WithSummary("Which policy applies to an employment on a day, why, and the annual leave it gives")
            .ProducesProblem(StatusCodes.Status404NotFound);

        // HR's preview of an employment in scope; the person's own comes with self service (PLAN L5, D42)
        leave.MapPost("/duration-preview", (PreviewLeaveDurationQuery query, ISender sender, CancellationToken ct) => OkAsync(sender.Send(query, ct)))
            .RequirePermission(HrPermissions.Employees.Read)
            .WithName("PreviewLeaveDuration")
            .WithSummary("The length of a leave on the employee's work schedule, with each day's share, before it is asked for")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut(
                "/employees/{employeeId:guid}/employments/{employmentId:guid}/leave-policy",
                (Guid employeeId, Guid employmentId, SetEmploymentLeavePolicyCommand command, ISender sender, CancellationToken ct) =>
                    NoContentAsync(sender.Send(command with { EmployeeId = employeeId, EmploymentId = employmentId }, ct)))
            .RequirePermission(HrPermissions.Leave.Settings.Manage)
            .WithName("SetEmploymentLeavePolicy")
            .WithSummary("Give an employment a leave policy by hand, or with null let the company's rules decide")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Results<Ok<T>, ProblemHttpResult>> OkAsync<T>(Task<Result<T>> sending)
    {
        var result = await sending;
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> CreatedAsync(Task<Result<Guid>> sending, string path)
    {
        var result = await sending;
        return result.IsSuccess
            ? TypedResults.Created($"{path}/{result.Value}", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> NoContentAsync(Task<Result> sending)
    {
        var result = await sending;
        return result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
    }
}
