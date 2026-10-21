using Merp.Application.Abstractions.Messaging;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Employees;
using Merp.SharedKernel.Results;
using Merp.Web.Endpoints;
using Merp.Web.Errors;
using Merp.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Merp.Modules.HR.Api.Endpoints;

/// <summary>Commands of the employee card and life cycle (PLAN H2a, D38); the queries come with H2b.</summary>
internal static class EmployeeEndpoints
{
    public static void MapEmployeeEndpoints(this IEndpointRouteBuilder group)
    {
        var employees = group.MapGroup("/employees");

        employees.MapPost("/", HireAsync)
            .RequirePermission(HrPermissions.Employees.Create)
            .WithName("HireEmployee")
            .WithSummary("Hire a new person in a company of the caller's scope: the employee, the employment and the first assignment")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        employees.MapPut("/{id:guid}/person", (Guid id, UpdateEmployeePersonCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { EmployeeId = id }, ct)))
            .RequirePermission(HrPermissions.Employees.Update)
            .WithName("UpdateEmployeePerson")
            .WithSummary("Update a person's identity details and custom fields; the TCKN never changes")
            .ProducesUpdateProblems();

        employees.MapPut("/{id:guid}/contacts", (Guid id, UpdateEmployeeContactsCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { EmployeeId = id }, ct)))
            .RequirePermission(HrPermissions.Employees.Update)
            .WithName("UpdateEmployeeContacts")
            .WithSummary("Replace a person's e-mail addresses, phone numbers, addresses and emergency contacts")
            .ProducesUpdateProblems();

        employees.MapPut("/{id:guid}/family", (Guid id, UpdateEmployeeFamilyCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { EmployeeId = id }, ct)))
            .RequirePermission(HrPermissions.Employees.Update)
            .WithName("UpdateEmployeeFamily")
            .WithSummary("Replace a person's family members")
            .ProducesUpdateProblems();

        employees.MapPut("/{id:guid}/qualifications", (Guid id, UpdateEmployeeQualificationsCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { EmployeeId = id }, ct)))
            .RequirePermission(HrPermissions.Employees.Update)
            .WithName("UpdateEmployeeQualifications")
            .WithSummary("Replace a person's education, certificates and driving licences, and languages")
            .ProducesUpdateProblems();

        employees.MapPut("/{id:guid}/iban", (Guid id, SetEmployeeIbanCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { EmployeeId = id }, ct)))
            .RequirePermission(HrPermissions.Employees.Sensitive.Update)
            .WithName("SetEmployeeIban")
            .WithSummary("Set or clear a person's IBAN (sensitive data)")
            .ProducesUpdateProblems();

        employees.MapPut("/{id:guid}/sensitive-data", (Guid id, UpdateEmployeeSensitiveDataCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { EmployeeId = id }, ct)))
            .RequirePermission(HrPermissions.Employees.Sensitive.Update)
            .WithName("UpdateEmployeeSensitiveData")
            .WithSummary("Replace a person's special category data; stored encrypted (D13)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        employees.MapPost("/{id:guid}/employments", RehireAsync)
            .RequirePermission(HrPermissions.Employees.Create)
            .WithName("RehireEmployee")
            .WithSummary("Rehire a person who left, in a company of the caller's scope; returns the new employment")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        employees.MapPost("/{id:guid}/assignments", ChangeAssignmentAsync)
            .RequirePermission(HrPermissions.Employees.Update)
            .WithName("ChangeEmployeeAssignment")
            .WithSummary("Start a new assignment of an open employment, possibly in the future; returns the assignment")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        employees.MapPost("/{id:guid}/terminate", (Guid id, TerminateEmploymentCommand command, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(command with { EmployeeId = id }, ct)))
            .RequirePermission(HrPermissions.Employees.Terminate)
            .WithName("TerminateEmployment")
            .WithSummary("End an employment on its last working day with an SGK leaving code")
            .ProducesUpdateProblems();

        employees.MapPost("/{id:guid}/group-transfer", GroupTransferAsync)
            .RequirePermission(HrPermissions.Employees.Update)
            .WithName("GroupTransferEmployee")
            .WithSummary("Move a person to another company of the group; returns the new employment")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static RouteHandlerBuilder ProducesUpdateProblems(this RouteHandlerBuilder builder) =>
        builder
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<Results<NoContent, ProblemHttpResult>> NoContentAsync(Task<Result> sending)
    {
        var result = await sending;
        return result.IsSuccess ? TypedResults.NoContent() : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> HireAsync(
        HireEmployeeCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/hr/employees/{result.Value}", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> RehireAsync(
        Guid id,
        RehireEmployeeCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command with { EmployeeId = id }, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/hr/employees/{id}", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> ChangeAssignmentAsync(
        Guid id,
        ChangeAssignmentCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command with { EmployeeId = id }, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/hr/employees/{id}", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<Results<Created<EntityCreatedResponse>, ProblemHttpResult>> GroupTransferAsync(
        Guid id,
        GroupTransferCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command with { EmployeeId = id }, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/hr/employees/{id}", new EntityCreatedResponse(result.Value))
            : (ProblemHttpResult)result.Error.ToProblem();
    }
}
