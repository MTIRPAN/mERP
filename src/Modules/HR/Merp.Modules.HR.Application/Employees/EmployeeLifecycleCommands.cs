using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.CustomFields;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Numbering;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Settings;
using Merp.Application.Abstractions.Validation;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Application.PreHires;
using Merp.Modules.HR.Contracts.Employees;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.PreHires;
using Merp.Modules.Platform.Contracts.Calendars;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Employees;

/// <summary>
/// Hires a new person (işe giriş, D38): the employee, their first employment in a company of the caller's scope and
/// their first assignment from the hire date. A TCKN already registered is refused; such a person is rehired.
/// </summary>
/// <remarks>
/// CustomFields holds the values of the tenant's custom fields by code (D30); PreHireId completes a waiting pre-hire of
/// the company (D54).
/// </remarks>
public sealed record HireEmployeeCommand(
    string? Tckn,
    string? FirstName,
    string? LastName,
    Guid CompanyId,
    DateOnly HireDate,
    EmploymentType? EmploymentType,
    Guid PositionId,
    DateOnly? BirthDate = null,
    string? BirthPlace = null,
    Gender? Gender = null,
    string? Nationality = null,
    MaritalStatus? MaritalStatus = null,
    MilitaryStatus? MilitaryStatus = null,
    DateOnly? SeniorityDate = null,
    string? SeniorityReason = null,
    DateOnly? ContractEndDate = null,
    decimal? WeeklyHours = null,
    DateOnly? ProbationEndDate = null,
    Guid? WorkScheduleId = null,
    Guid? BranchId = null,
    Guid? ManagerPositionId = null,
    string? Level = null,
    string? CostCenterCode = null,
    IReadOnlyDictionary<string, JsonElement>? CustomFields = null,
    Guid? PreHireId = null)
    : ICommand<Guid>, IRequirePermission, IPersonFields, IEmploymentTermsFields, IAssignmentFields
{
    [JsonIgnore]
    public string Permission => HrPermissions.Employees.Create;
}

/// <summary>A new employment of a person who left, in the same or another company (tekrar işe giriş). Returns the employment.</summary>
public sealed record RehireEmployeeCommand(
    Guid CompanyId,
    DateOnly HireDate,
    EmploymentType? EmploymentType,
    Guid PositionId,
    DateOnly? SeniorityDate = null,
    string? SeniorityReason = null,
    DateOnly? ContractEndDate = null,
    decimal? WeeklyHours = null,
    DateOnly? ProbationEndDate = null,
    Guid? WorkScheduleId = null,
    Guid? BranchId = null,
    Guid? ManagerPositionId = null,
    string? Level = null,
    string? CostCenterCode = null,
    Guid? PreHireId = null) : ICommand<Guid>, IRequirePermission, IEmploymentTermsFields, IAssignmentFields
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid EmployeeId { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Employees.Create;
}

/// <summary>
/// A new assignment of an open employment (görev değişikliği), possibly in the future: it takes effect on its first
/// day. A change on the latest assignment's first day corrects it. Returns the assignment.
/// </summary>
public sealed record ChangeEmployeeAssignmentCommand(
    Guid EmploymentId,
    DateOnly ValidFrom,
    AssignmentReason? Reason,
    Guid PositionId,
    Guid? BranchId = null,
    Guid? ManagerPositionId = null,
    string? Level = null,
    string? CostCenterCode = null,
    Guid? PreHireId = null) : ICommand<Guid>, IRequirePermission, IAssignmentFields
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid EmployeeId { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Employees.Update;
}

/// <summary>Ends an employment on its last working day (işten çıkış) with an SGK leaving code.</summary>
public sealed record TerminateEmploymentCommand(
    Guid EmploymentId,
    DateOnly Date,
    string? SgkExitCode,
    bool Rehireable,
    string? Reason = null,
    string? Note = null) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid EmployeeId { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Employees.Terminate;
}

/// <summary>
/// Moves the person to another company of the group (holding içi nakil): the source employment ends the day before
/// <see cref="TransferDate"/> with the SGK code, a new one starts on it. Both companies must be in the caller's scope.
/// Returns the new employment.
/// </summary>
/// <remarks>KeepSeniority keeps the source's seniority date; TransferLeaveBalance tells the leave module to move the balance.</remarks>
public sealed record GroupTransferCommand(
    Guid EmploymentId,
    Guid TargetCompanyId,
    DateOnly TransferDate,
    string? SgkExitCode,
    EmploymentType? EmploymentType,
    Guid PositionId,
    bool KeepSeniority = true,
    bool TransferLeaveBalance = true,
    DateOnly? SeniorityDate = null,
    string? SeniorityReason = null,
    DateOnly? ContractEndDate = null,
    decimal? WeeklyHours = null,
    DateOnly? ProbationEndDate = null,
    Guid? WorkScheduleId = null,
    Guid? BranchId = null,
    Guid? ManagerPositionId = null,
    string? Level = null,
    string? CostCenterCode = null) : ICommand<Guid>, IRequirePermission, IEmploymentTermsFields, IAssignmentFields
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid EmployeeId { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Employees.Update;
}

internal sealed class HireEmployeeCommandValidator : AbstractValidator<HireEmployeeCommand>
{
    public HireEmployeeCommandValidator()
    {
        RuleFor(c => c.Tckn).Cascade(CascadeMode.Stop).NotEmpty().MustBeValid(Tckn.Create);
        RuleFor(c => c.CompanyId).NotEmpty();
        PersonRules.AddPersonRules(this);
        EmployeeRules.AddTermsRules(this);
        EmployeeRules.AddAssignmentRules(this);
    }
}

internal sealed class RehireEmployeeCommandValidator : AbstractValidator<RehireEmployeeCommand>
{
    public RehireEmployeeCommandValidator()
    {
        RuleFor(c => c.CompanyId).NotEmpty();
        EmployeeRules.AddTermsRules(this);
        EmployeeRules.AddAssignmentRules(this);
    }
}

internal sealed class ChangeEmployeeAssignmentCommandValidator : AbstractValidator<ChangeEmployeeAssignmentCommand>
{
    public ChangeEmployeeAssignmentCommandValidator()
    {
        RuleFor(c => c.EmploymentId).NotEmpty();
        RuleFor(c => c.Reason).Cascade(CascadeMode.Stop).NotNull().IsInEnum().NotEqual(AssignmentReason.Hire);
        EmployeeRules.AddAssignmentRules(this);
    }
}

internal sealed class TerminateEmploymentCommandValidator : AbstractValidator<TerminateEmploymentCommand>
{
    public TerminateEmploymentCommandValidator()
    {
        RuleFor(c => c.EmploymentId).NotEmpty();
        RuleFor(c => c.SgkExitCode).NotEmpty().MaximumLength(Employment.SgkExitCodeMaxLength);
        RuleFor(c => c.Reason).MaximumLength(Employment.TextMaxLength);
        RuleFor(c => c.Note).MaximumLength(Employment.TextMaxLength);
    }
}

internal sealed class GroupTransferCommandValidator : AbstractValidator<GroupTransferCommand>
{
    public GroupTransferCommandValidator()
    {
        RuleFor(c => c.EmploymentId).NotEmpty();
        RuleFor(c => c.TargetCompanyId).NotEmpty();
        RuleFor(c => c.SgkExitCode).NotEmpty().MaximumLength(Employment.SgkExitCodeMaxLength);
        EmployeeRules.AddTermsRules(this);
        EmployeeRules.AddAssignmentRules(this);
    }
}

internal sealed class HireEmployeeCommandHandler(
    IHrDbContext db,
    IAccessScopeProvider scopes,
    IOrganizationLookup organization,
    IWorkingCalendar calendar,
    ICustomFieldValidator customFields,
    INumberGenerator numbers,
    IIntegrationEventPublisher publisher) : ICommandHandler<HireEmployeeCommand, Guid>
{
    public async Task<Result<Guid>> Handle(HireEmployeeCommand request, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ForCompaniesAsync(scopes, request.Permission, cancellationToken);
        if (!scope.CoversCompany(request.CompanyId))
        {
            return CommonErrors.Forbidden;
        }

        if (await OrgReferences.CheckCompanyAsync(organization, request.CompanyId, cancellationToken) is { } companyError)
        {
            return Error.Validation([companyError]);
        }

        var tckn = Tckn.Create(request.Tckn).Value;
        var errors = new List<FieldError>();
        if (await db.Employees.AnyAsync(e => e.Tckn == tckn.Value, cancellationToken))
        {
            errors.Add(new FieldError("tckn", EmployeeCommandErrors.TcknExistsCode));
        }

        var assignment = await EmployeeRules.ResolveAssignmentAsync(db, organization, request, request.CompanyId, cancellationToken);
        var terms = await EmployeeRules.ResolveTermsAsync(calendar, request, request.CompanyId, request.HireDate, cancellationToken);
        var values = await customFields.ValidateAsync(HrCustomFieldEntities.Employee, request.CompanyId, request.CustomFields, CustomFieldValues.Empty, cancellationToken);
        errors.AddRange(new Result[] { assignment, terms, values }.Where(r => r.IsFailure).SelectMany(r => r.Error.Fields));
        if (await PreHireRules.CheckAsync(db, request.PreHireId, request.CompanyId, null, cancellationToken) is { } preHireError)
        {
            errors.Add(preHireError);
        }
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var registryNumber = await numbers.NextAsync(request.CompanyId, HrNumberSequences.Employee, request.HireDate, cancellationToken);
        var hired = Employee.Hire(tckn, PersonRules.ToProfile(request), terms.Value, assignment.Value, registryNumber);
        if (hired.IsFailure)
        {
            return hired.Error;
        }

        var employee = hired.Value;
        employee.SetCustomFields(values.Value);
        var employment = employee.Employments[0];
        var first = employment.Assignments[0];
        employee.MarkAnnounced(first.Id);
        if (request.PreHireId is { } preHireId)
        {
            employee.ConfirmPreHire(PreHireId.From(preHireId), employment.Id, employment.HireDate);
        }

        db.Employees.Add(employee);
        await publisher.PublishAsync(
            new EmployeeHired(employee.Id.Value, employment.Id.Value, employment.RegistryNumber, employment.HireDate, first.PositionId.Value, employee.UserId)
            {
                CompanyId = employment.CompanyId,
            },
            cancellationToken);
        return employee.Id.Value;
    }
}

internal sealed class RehireEmployeeCommandHandler(
    IHrDbContext db,
    IAccessScopeProvider scopes,
    IOrganizationLookup organization,
    IWorkingCalendar calendar,
    INumberGenerator numbers,
    IIntegrationEventPublisher publisher) : ICommandHandler<RehireEmployeeCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RehireEmployeeCommand request, CancellationToken cancellationToken)
    {
        // Rehiring needs only the new company in scope: the person may come from a company the caller cannot see
        var scope = await OrgScope.ForCompaniesAsync(scopes, request.Permission, cancellationToken);
        if (!scope.CoversCompany(request.CompanyId))
        {
            return CommonErrors.Forbidden;
        }

        var employee = await EmployeeRules.LoadAsync(db, request.EmployeeId, cancellationToken);
        if (employee is null)
        {
            return CommonErrors.NotFound;
        }

        if (await OrgReferences.CheckCompanyAsync(organization, request.CompanyId, cancellationToken) is { } companyError)
        {
            return Error.Validation([companyError]);
        }

        var assignment = await EmployeeRules.ResolveAssignmentAsync(db, organization, request, request.CompanyId, cancellationToken);
        var terms = await EmployeeRules.ResolveTermsAsync(calendar, request, request.CompanyId, request.HireDate, cancellationToken);
        var preHireError = await PreHireRules.CheckAsync(db, request.PreHireId, request.CompanyId, employee.Id, cancellationToken);
        if (assignment.IsFailure || terms.IsFailure || preHireError is not null)
        {
            return Error.Validation(new Result[] { assignment, terms }.Where(r => r.IsFailure).SelectMany(r => r.Error.Fields).Concat(preHireError is null ? [] : [preHireError]));
        }

        var registryNumber = await numbers.NextAsync(request.CompanyId, HrNumberSequences.Employee, request.HireDate, cancellationToken);
        var rehired = employee.Rehire(terms.Value, assignment.Value, registryNumber);
        if (rehired.IsFailure)
        {
            return rehired.Error;
        }

        var employment = rehired.Value;
        var first = employment.Assignments[0];
        employee.MarkAnnounced(first.Id);
        if (request.PreHireId is { } preHireId)
        {
            employee.ConfirmPreHire(PreHireId.From(preHireId), employment.Id, employment.HireDate);
        }
        await publisher.PublishAsync(
            new EmployeeRehired(employee.Id.Value, employment.Id.Value, employment.RegistryNumber, employment.HireDate, first.PositionId.Value, employee.UserId)
            {
                CompanyId = employment.CompanyId,
            },
            cancellationToken);
        return employment.Id.Value;
    }
}

internal sealed class ChangeEmployeeAssignmentCommandHandler(
    IHrDbContext db,
    IAccessScopeProvider scopes,
    IOrganizationLookup organization,
    TimeProvider timeProvider,
    IIntegrationEventPublisher publisher) : ICommandHandler<ChangeEmployeeAssignmentCommand, Guid>
{
    public async Task<Result<Guid>> Handle(ChangeEmployeeAssignmentCommand request, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ForCompaniesAsync(scopes, request.Permission, cancellationToken);
        var employee = await EmployeeRules.FindAsync(db, scope, request.EmployeeId, cancellationToken);
        var employment = employee?.Employments.SingleOrDefault(m => m.Id == EmploymentId.From(request.EmploymentId));
        if (employee is null || employment is null || !scope.CoversCompany(employment.CompanyId))
        {
            return CommonErrors.NotFound;
        }

        var details = await EmployeeRules.ResolveAssignmentAsync(db, organization, request, employment.CompanyId, cancellationToken);
        if (details.IsFailure)
        {
            return details.Error;
        }

        if (await PreHireRules.CheckAsync(db, request.PreHireId, employment.CompanyId, employee.Id, cancellationToken) is { } preHireError)
        {
            return Error.Validation([preHireError]);
        }

        var changed = employee.ChangeAssignment(employment.Id, details.Value, request.ValidFrom, request.Reason!.Value);
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        var assignment = changed.Value;
        if (request.PreHireId is { } preHireId)
        {
            employee.ConfirmPreHire(PreHireId.From(preHireId), employment.Id, request.ValidFrom);
        }
        await EmployeeEvents.AnnounceIfStartedAsync(db, organization, publisher, timeProvider, employee, employment, assignment, cancellationToken);
        return assignment.Id.Value;
    }
}

internal sealed class TerminateEmploymentCommandHandler(
    IHrDbContext db,
    IAccessScopeProvider scopes,
    IIntegrationEventPublisher publisher,
    ISettings settings,
    TimeProvider timeProvider) : ICommandHandler<TerminateEmploymentCommand>
{
    public async Task<Result> Handle(TerminateEmploymentCommand request, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ForCompaniesAsync(scopes, request.Permission, cancellationToken);
        var employee = await EmployeeRules.FindAsync(db, scope, request.EmployeeId, cancellationToken);
        var employment = employee?.Employments.SingleOrDefault(m => m.Id == EmploymentId.From(request.EmploymentId));
        if (employee is null || employment is null || !scope.CoversCompany(employment.CompanyId))
        {
            return CommonErrors.NotFound;
        }

        if (await EmployeeRules.CheckSgkExitCodeAsync(db, request.SgkExitCode!, "sgkExitCode", cancellationToken) is { } codeError)
        {
            return Error.Validation([codeError]);
        }

        var details = new TerminationDetails(request.Date, request.SgkExitCode!.Trim().ToUpperInvariant(), request.Reason, request.Rehireable, request.Note);
        var ended = employee.Terminate(employment.Id, details);
        if (ended.IsFailure)
        {
            return ended;
        }

        await publisher.PublishAsync(
            new EmployeeTerminated(
                employee.Id.Value,
                employment.Id.Value,
                request.Date,
                details.SgkExitCode,
                employee.UserId,
                employee.Employments.Any(m => m.IsOpen))
            {
                CompanyId = employment.CompanyId,
            },
            cancellationToken);
        await EmployeeUserRules.RequestImmediateDeactivationAsync(employee, employment.CompanyId, settings, publisher, timeProvider, cancellationToken);
        return Result.Success();
    }
}

internal sealed class GroupTransferCommandHandler(
    IHrDbContext db,
    IAccessScopeProvider scopes,
    IOrganizationLookup organization,
    IWorkingCalendar calendar,
    INumberGenerator numbers,
    IIntegrationEventPublisher publisher) : ICommandHandler<GroupTransferCommand, Guid>
{
    public async Task<Result<Guid>> Handle(GroupTransferCommand request, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ForCompaniesAsync(scopes, request.Permission, cancellationToken);
        var employee = await EmployeeRules.FindAsync(db, scope, request.EmployeeId, cancellationToken);
        var source = employee?.Employments.SingleOrDefault(m => m.Id == EmploymentId.From(request.EmploymentId));
        if (employee is null || source is null || !scope.CoversCompany(source.CompanyId))
        {
            return CommonErrors.NotFound;
        }

        if (!scope.CoversCompany(request.TargetCompanyId))
        {
            return CommonErrors.Forbidden;
        }

        if (await OrgReferences.CheckCompanyAsync(organization, request.TargetCompanyId, cancellationToken) is { } companyError)
        {
            return Error.Validation([companyError with { Field = "targetCompanyId" }]);
        }

        var errors = new List<FieldError>();
        if (await EmployeeRules.CheckSgkExitCodeAsync(db, request.SgkExitCode!, "sgkExitCode", cancellationToken) is { } codeError)
        {
            errors.Add(codeError);
        }

        var assignment = await EmployeeRules.ResolveAssignmentAsync(db, organization, request, request.TargetCompanyId, cancellationToken);
        var terms = await EmployeeRules.ResolveTermsAsync(calendar, request, request.TargetCompanyId, request.TransferDate, cancellationToken);
        errors.AddRange(new Result[] { assignment, terms }.Where(r => r.IsFailure).SelectMany(r => r.Error.Fields));
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var registryNumber = await numbers.NextAsync(request.TargetCompanyId, HrNumberSequences.Employee, request.TransferDate, cancellationToken);
        var moved = employee.GroupTransfer(
            source.Id,
            terms.Value,
            request.SgkExitCode!.Trim().ToUpperInvariant(),
            request.KeepSeniority,
            assignment.Value,
            registryNumber,
            request.TransferLeaveBalance);
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        var target = moved.Value;
        employee.MarkAnnounced(target.Assignments[0].Id);
        await publisher.PublishAsync(
            new EmployeeGroupTransferred(
                employee.Id.Value,
                source.Id.Value,
                source.CompanyId,
                target.Id.Value,
                request.TransferDate,
                request.KeepSeniority,
                request.TransferLeaveBalance)
            {
                CompanyId = target.CompanyId,
            },
            cancellationToken);
        return target.Id.Value;
    }
}

/// <summary>Announcing assignment changes to other modules (D38).</summary>
internal static class EmployeeEvents
{
    /// <summary>
    /// Publishes <see cref="EmployeeAssignmentChanged"/> when the assignment has started in the company's time zone;
    /// a later one is announced by the nightly job on its first day.
    /// </summary>
    public static async Task AnnounceIfStartedAsync(
        IHrDbContext db,
        IOrganizationLookup organization,
        IIntegrationEventPublisher publisher,
        TimeProvider timeProvider,
        Employee employee,
        Employment employment,
        JobAssignment assignment,
        CancellationToken cancellationToken)
    {
        var company = (await organization.GetCompaniesAsync([employment.CompanyId], cancellationToken)).SingleOrDefault();
        if (assignment.ValidFrom > HrClock.Today(timeProvider, company?.TimeZoneId))
        {
            return;
        }

        await PublishAsync(db, publisher, employee, employment, assignment, cancellationToken);
    }

    public static async Task PublishAsync(
        IHrDbContext db,
        IIntegrationEventPublisher publisher,
        Employee employee,
        Employment employment,
        JobAssignment assignment,
        CancellationToken cancellationToken)
    {
        var unit = await db.Positions.AsNoTracking()
            .Where(p => p.Id == assignment.PositionId)
            .Select(p => p.OrgUnitId)
            .SingleAsync(cancellationToken);
        employee.MarkAnnounced(assignment.Id);
        await publisher.PublishAsync(
            new EmployeeAssignmentChanged(
                employee.Id.Value,
                employment.Id.Value,
                assignment.Id.Value,
                assignment.ValidFrom,
                assignment.PositionId.Value,
                unit.Value,
                assignment.Reason.ToString())
            {
                CompanyId = employment.CompanyId,
            },
            cancellationToken);
    }
}
