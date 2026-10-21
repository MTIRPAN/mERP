using Merp.Application.Abstractions.Exports;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Paging;
using Merp.Modules.HR.Application;
using Merp.Modules.HR.Application.Employees;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.Platform.Contracts.Users;
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

/// <summary>The employee list, card, photo and life cycle (PLAN H2a, H2b, D38).</summary>
internal static class EmployeeEndpoints
{
    public static void MapEmployeeEndpoints(this IEndpointRouteBuilder group)
    {
        var employees = group.MapGroup("/employees");

        employees.MapGet("/", ListAsync)
            .RequirePermission(HrPermissions.Employees.Read)
            .WithName("ListEmployees")
            .WithSummary("Employments in the caller's scope, one row per person and company; who works today unless a status is given")
            .ProducesValidationProblem();

        employees.MapGet("/export", ExportAsync)
            .RequirePermission(HrPermissions.Employees.Export)
            .WithName("ExportEmployees")
            .WithSummary("The employee list as Excel, with the list's filters and sort and the chosen columns (D35)")
            .ProducesValidationProblem();

        employees.MapGet("/user-candidates", SearchUserCandidatesAsync)
            .RequirePermission(HrPermissions.Employees.User)
            .WithName("SearchEmployeeUserCandidates")
            .WithSummary("Active users not linked to a person yet, by name, user name or e-mail, to link to an employee");

        employees.MapPost("/{id:guid}/user", (Guid id, CreateEmployeeUserCommand command, ISender sender, CancellationToken ct) =>
                AcceptedAsync(sender.Send(command with { EmployeeId = id }, ct)))
            .RequirePermission(HrPermissions.Employees.User)
            .WithName("CreateEmployeeUser")
            .WithSummary("Ask for a system user of the person with the Employee role; Platform answers later (D39)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        employees.MapPost("/{id:guid}/user/link", (Guid id, LinkEmployeeUserCommand command, ISender sender, CancellationToken ct) =>
                AcceptedAsync(sender.Send(command with { EmployeeId = id }, ct)))
            .RequirePermission(HrPermissions.Employees.User)
            .WithName("LinkEmployeeUser")
            .WithSummary("Ask to link the person to an existing user; Platform answers later (D39)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        employees.MapDelete("/{id:guid}/user", (Guid id, ISender sender, CancellationToken ct) =>
                AcceptedAsync(sender.Send(new UnlinkEmployeeUserCommand(id), ct)))
            .RequirePermission(HrPermissions.Employees.User)
            .WithName("UnlinkEmployeeUser")
            .WithSummary("Ask to remove the link of the person and their user; the user keeps their roles")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        employees.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(HrPermissions.Employees.Read)
            .WithName("GetEmployee")
            .WithSummary("The employee card; sensitive values are masked without hr.employees.sensitive.read and audited when shown")
            .ProducesProblem(StatusCodes.Status404NotFound);

        employees.MapGet("/{id:guid}/photo", GetPhotoAsync)
            .RequirePermission(HrPermissions.Employees.Read)
            .WithName("GetEmployeePhoto")
            .WithSummary("The person's photo; 404 when there is none")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);

        employees.MapPut("/{id:guid}/photo", SetPhotoAsync)
            .RequirePermission(HrPermissions.Employees.Update)
            .WithMetadata(new RequestSizeLimitAttribute(FileTransfer.RequestLimit(SetEmployeePhotoCommand.MaxBytes)))
            // Authentication is a bearer token, never a cookie, so cross-site request forgery does not apply.
            .DisableAntiforgery()
            .WithName("SetEmployeePhoto")
            .WithSummary("Set or replace the person's photo: PNG, JPEG or WebP, at most 2 MB")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        employees.MapDelete("/{id:guid}/photo", (Guid id, ISender sender, CancellationToken ct) =>
                NoContentAsync(sender.Send(new RemoveEmployeePhotoCommand(id), ct)))
            .RequirePermission(HrPermissions.Employees.Update)
            .WithName("RemoveEmployeePhoto")
            .WithSummary("Remove the person's photo")
            .ProducesProblem(StatusCodes.Status404NotFound);

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

    public static void MapSgkExitCodeEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/sgk-exit-codes", ListSgkExitCodesAsync)
            .RequirePermission(HrPermissions.Employees.Read)
            .WithName("ListSgkExitCodes")
            .WithSummary("The SGK leaving codes of the tenant, active ones unless asked");
    }

    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Rows per page, at most 200.</param>
    /// <param name="sort">firstName, lastName, registryNumber, hireDate, terminationDate or createdAt; a leading "-" sorts descending.</param>
    /// <param name="companyId">Only employments of this company.</param>
    /// <param name="branchId">Only people whose assignment is in this branch.</param>
    /// <param name="orgUnitId">Only people whose position is in this unit.</param>
    /// <param name="includeSubUnits">With orgUnitId: also the people of its sub units.</param>
    /// <param name="positionId">Only the holders of this position.</param>
    /// <param name="jobId">Only people with this job.</param>
    /// <param name="status">Employment states on the company's day (repeatable); Active when absent.</param>
    /// <param name="search">Part of the name or of the registry number, or a whole TCKN.</param>
    /// <param name="customFields">JSON object of custom field codes and values the people must have.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<PagedResult<EmployeeListItem>>, ProblemHttpResult>> ListAsync(
        int? page,
        int? pageSize,
        string? sort,
        Guid? companyId,
        Guid? branchId,
        Guid? orgUnitId,
        bool? includeSubUnits,
        Guid? positionId,
        Guid? jobId,
        EmploymentStatus[]? status,
        string? search,
        string? customFields,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetEmployeesQuery(new PageRequest(page, pageSize, sort), companyId, branchId, orgUnitId, includeSubUnits ?? false, positionId, jobId, status, search, customFields),
            cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    /// <param name="columns">Column keys, comma separated (registryNumber,firstName); every column when empty.</param>
    /// <param name="sort">The list's sort.</param>
    /// <param name="companyId">The list's filter.</param>
    /// <param name="branchId">The list's filter.</param>
    /// <param name="orgUnitId">The list's filter.</param>
    /// <param name="includeSubUnits">The list's filter.</param>
    /// <param name="positionId">The list's filter.</param>
    /// <param name="jobId">The list's filter.</param>
    /// <param name="status">The list's filter.</param>
    /// <param name="search">The list's filter.</param>
    /// <param name="customFields">The list's custom field filter.</param>
    /// <param name="exporter">Writes the file.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> ExportAsync(
        string? columns,
        string? sort,
        Guid? companyId,
        Guid? branchId,
        Guid? orgUnitId,
        bool? includeSubUnits,
        Guid? positionId,
        Guid? jobId,
        EmploymentStatus[]? status,
        string? search,
        string? customFields,
        IListExporter exporter,
        CancellationToken cancellationToken)
    {
        var result = await exporter.ExportAsync(
            new ExportList<EmployeeListItem>(
                "web.hr.employees.title",
                typeof(Employee),
                EmployeeExports.Columns,
                paging => new GetEmployeesQuery(paging, companyId, branchId, orgUnitId, includeSubUnits ?? false, positionId, jobId, status, search, customFields),
                sort,
                ExportRules.ParseColumns(columns),
                new Dictionary<string, object?>
                {
                    ["companyId"] = companyId,
                    ["branchId"] = branchId,
                    ["orgUnitId"] = orgUnitId,
                    ["includeSubUnits"] = includeSubUnits,
                    ["positionId"] = positionId,
                    ["jobId"] = jobId,
                    ["status"] = status is null ? null : string.Join(",", status),
                    ["search"] = search,
                    ["customFields"] = customFields,
                }),
            cancellationToken);
        return result.ToFile();
    }

    private static async Task<Results<Ok<EmployeeDetails>, ProblemHttpResult>> GetAsync(Guid id, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetEmployeeQuery(id), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static async Task<IResult> GetPhotoAsync(Guid id, HttpContext context, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetEmployeePhotoQuery(id), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        var file = result.Value;
        return FileTransfer.Content(context, file.FileName, file.ContentType, file.Length, file.Content);
    }

    /// <param name="id">The person.</param>
    /// <param name="file">PNG, JPEG or WebP, at most 2 MB.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<EntityCreatedResponse>, ProblemHttpResult>> SetPhotoAsync(
        Guid id,
        IFormFile? file,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new SetEmployeePhotoCommand(id, file.ToUpload()), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(new EntityCreatedResponse(result.Value)) : (ProblemHttpResult)result.Error.ToProblem();
    }

    /// <param name="includeInactive">Also the codes that are no longer used.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<IReadOnlyList<SgkExitCodeItem>>, ProblemHttpResult>> ListSgkExitCodesAsync(
        bool? includeInactive,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetSgkExitCodesQuery(includeInactive ?? false), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

    private static RouteHandlerBuilder ProducesUpdateProblems(this RouteHandlerBuilder builder) =>
        builder
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    /// <summary>202: the request is on its way to Platform; the card shows it as pending until the answer.</summary>
    private static async Task<Results<Accepted, ProblemHttpResult>> AcceptedAsync(Task<Result> sending)
    {
        var result = await sending;
        return result.IsSuccess ? TypedResults.Accepted((string?)null) : (ProblemHttpResult)result.Error.ToProblem();
    }

    /// <param name="search">Part of the name, user name or e-mail.</param>
    /// <param name="sender">Mediator.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    private static async Task<Results<Ok<IReadOnlyList<UserSummary>>, ProblemHttpResult>> SearchUserCandidatesAsync(
        string? search,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new SearchUserCandidatesQuery(search), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : (ProblemHttpResult)result.Error.ToProblem();
    }

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
        ChangeEmployeeAssignmentCommand command,
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
