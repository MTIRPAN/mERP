using FluentValidation;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Organization;
using Merp.Modules.Platform.Contracts.Calendars;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Employees;

/// <summary>Assignment fields of hiring, rehiring, assignment changes and group transfers.</summary>
public interface IAssignmentFields
{
    /// <summary>An active position of the employment's company.</summary>
    Guid PositionId { get; }

    /// <summary>A branch of the company.</summary>
    Guid? BranchId { get; }

    /// <summary>A position the person reports to instead of their position's manager (yönetici ezmesi).</summary>
    Guid? ManagerPositionId { get; }

    /// <summary>The job's level when not given.</summary>
    string? Level { get; }

    /// <summary>The unit's cost center when not given.</summary>
    string? CostCenterCode { get; }
}

/// <summary>Employment terms of hiring, rehiring and group transfers (the transfer date is the hire date).</summary>
public interface IEmploymentTermsFields
{
    DateOnly? SeniorityDate { get; }

    string? SeniorityReason { get; }

    EmploymentType? EmploymentType { get; }

    DateOnly? ContractEndDate { get; }

    decimal? WeeklyHours { get; }

    DateOnly? ProbationEndDate { get; }

    /// <summary>A work schedule overriding the branch's and the company's (P9).</summary>
    Guid? WorkScheduleId { get; }
}

/// <summary>Error codes of employees; texts in the module's <c>tr-TR.json</c>.</summary>
public static class EmployeeCommandErrors
{
    public const string TcknExistsCode = "hr.employees.errors.tckn_exists";
    public const string InvalidPositionCode = "hr.employees.errors.invalid_position";
    public const string InvalidManagerPositionCode = "hr.employees.errors.invalid_manager_position";
    public const string UnknownWorkScheduleCode = "hr.employees.errors.unknown_work_schedule";
    public const string UnknownSgkExitCode = "hr.employees.errors.unknown_sgk_exit_code";
    public const string PrimaryCountCode = "hr.employees.errors.one_primary";
}

internal static class EmployeeRules
{
    /// <summary>
    /// The person with their employments and job history, tracked; null when missing or none of their companies is in
    /// the caller's scope (outside the scope looks the same as missing).
    /// </summary>
    public static async Task<Employee?> FindAsync(IHrDbContext db, OrgScope scope, Guid id, CancellationToken cancellationToken)
    {
        var employee = await LoadAsync(db, id, cancellationToken);
        return employee is not null && employee.Employments.Any(m => scope.CoversCompany(m.CompanyId)) ? employee : null;
    }

    public static Task<Employee?> LoadAsync(IHrDbContext db, Guid id, CancellationToken cancellationToken) =>
        db.Employees
            .Include(e => e.Employments)
            .ThenInclude(m => m.Assignments)
            .SingleOrDefaultAsync(e => e.Id == EmployeeId.From(id), cancellationToken);

    public static void AddAssignmentRules<T>(AbstractValidator<T> validator)
        where T : IAssignmentFields
    {
        validator.RuleFor(c => c.PositionId).NotEmpty();
        validator.RuleFor(c => c.Level).MaximumLength(JobAssignment.LevelMaxLength);
        validator.RuleFor(c => c.CostCenterCode).MaximumLength(JobAssignment.CostCenterCodeMaxLength);
    }

    public static void AddTermsRules<T>(AbstractValidator<T> validator)
        where T : IEmploymentTermsFields
    {
        validator.RuleFor(c => c.EmploymentType).Cascade(CascadeMode.Stop).NotNull().IsInEnum();
        validator.RuleFor(c => c.SeniorityReason).MaximumLength(Employment.TextMaxLength);
    }

    /// <summary>
    /// The assignment of <paramref name="input"/> in <paramref name="companyId"/>: an active position of the
    /// company, its job and the job's level, the unit's cost center unless given, and a manager position and a branch
    /// of the same company.
    /// </summary>
    public static async Task<Result<AssignmentDetails>> ResolveAssignmentAsync(
        IHrDbContext db,
        IOrganizationLookup organization,
        IAssignmentFields input,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        var positionId = PositionId.From(input.PositionId);
        var position = await db.Positions.AsNoTracking()
            .Where(p => p.Id == positionId)
            .Select(p => new
            {
                p.CompanyId,
                p.Status,
                p.JobId,
                JobLevel = db.Jobs.Where(j => j.Id == p.JobId).Select(j => j.Level).First(),
                CostCenter = db.OrgUnits.Where(u => u.Id == p.OrgUnitId).Select(u => u.CostCenterCode).First(),
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (position is null || position.CompanyId != companyId || position.Status != PositionStatus.Active)
        {
            errors.Add(new FieldError("positionId", EmployeeCommandErrors.InvalidPositionCode));
        }

        var managerId = input.ManagerPositionId is { } id ? PositionId.From(id) : (PositionId?)null;
        if (managerId is { } manager
            && (manager == positionId
                || !await db.Positions.AsNoTracking().AnyAsync(p => p.Id == manager && p.CompanyId == companyId && p.Status != PositionStatus.Closed, cancellationToken)))
        {
            errors.Add(new FieldError("managerPositionId", EmployeeCommandErrors.InvalidManagerPositionCode));
        }

        if (await OrgReferences.CheckBranchAsync(organization, input.BranchId, companyId, cancellationToken) is { } branchError)
        {
            errors.Add(branchError);
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        return new AssignmentDetails(
            positionId,
            input.BranchId,
            managerId,
            position!.JobId,
            string.IsNullOrWhiteSpace(input.Level) ? position.JobLevel : input.Level,
            string.IsNullOrWhiteSpace(input.CostCenterCode) ? position.CostCenter : input.CostCenterCode);
    }

    /// <summary>Terms of <paramref name="input"/> starting on <paramref name="hireDate"/>; the work schedule must exist.</summary>
    public static async Task<Result<EmploymentTerms>> ResolveTermsAsync(
        IWorkingCalendar calendar,
        IEmploymentTermsFields input,
        Guid companyId,
        DateOnly hireDate,
        CancellationToken cancellationToken)
    {
        if (input.WorkScheduleId is { } scheduleId && await calendar.GetDayAsync(scheduleId, hireDate, cancellationToken) is null)
        {
            return Error.Validation([new FieldError("workScheduleId", EmployeeCommandErrors.UnknownWorkScheduleCode)]);
        }

        return new EmploymentTerms(
            companyId,
            hireDate,
            input.SeniorityDate,
            input.SeniorityReason,
            input.EmploymentType!.Value,
            input.ContractEndDate,
            input.WeeklyHours,
            input.ProbationEndDate,
            input.WorkScheduleId,
            null);
    }

    public static async Task<FieldError?> CheckSgkExitCodeAsync(IHrDbContext db, string code, string field, CancellationToken cancellationToken)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return await db.SgkExitCodes.AsNoTracking().AnyAsync(c => c.Code == normalized && c.IsActive, cancellationToken)
            ? null
            : new FieldError(field, EmployeeCommandErrors.UnknownSgkExitCode);
    }
}
