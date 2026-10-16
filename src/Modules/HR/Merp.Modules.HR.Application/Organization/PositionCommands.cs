using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Validation;
using Merp.Application.Persistence;
using Merp.Modules.HR.Domain.Organization;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Organization;

/// <summary>
/// Creates an active position in a unit; its company is the unit's. Needs <c>hr.org.manage</c> with the Tenant
/// scope or the company's scope (D37).
/// </summary>
/// <param name="OrgUnitId">The unit.</param>
/// <param name="JobId">An active job of the catalog.</param>
/// <param name="Code">Unique within the company.</param>
/// <param name="ReportsToPositionId">An open position of the company; null follows the unit's manager.</param>
/// <param name="Norm">Number of seats, 1 when not given.</param>
/// <param name="IsManager">A managerial position.</param>
public sealed record CreatePositionCommand(
    Guid OrgUnitId,
    Guid JobId,
    string? Code,
    Guid? ReportsToPositionId = null,
    int? Norm = null,
    bool IsManager = false) : ICommand<Guid>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => HrPermissions.Org.Manage;
}

/// <summary>
/// Updates a position: unit (of the same company), job, manager, seats, status and the manager flag. The company and
/// the code never change. A position that manages a unit cannot be closed or leave its unit.
/// </summary>
/// <remarks>Version is the one the client read; a concurrent change ends in 409.</remarks>
public sealed record UpdatePositionCommand(
    uint Version,
    Guid OrgUnitId,
    Guid JobId,
    Guid? ReportsToPositionId,
    int Norm,
    PositionStatus? Status,
    bool IsManager) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Org.Manage;
}

internal sealed class CreatePositionCommandValidator : AbstractValidator<CreatePositionCommand>
{
    public CreatePositionCommandValidator()
    {
        RuleFor(p => p.OrgUnitId).NotEmpty();
        RuleFor(p => p.JobId).NotEmpty();
        RuleFor(p => p.Code).Cascade(CascadeMode.Stop).NotEmpty().MustBeValid(v => Code.Create(v, Position.CodeMaxLength));
        RuleFor(p => p.Norm).InclusiveBetween(1, Position.MaxNorm);
    }
}

internal sealed class UpdatePositionCommandValidator : AbstractValidator<UpdatePositionCommand>
{
    public UpdatePositionCommandValidator()
    {
        RuleFor(p => p.OrgUnitId).NotEmpty();
        RuleFor(p => p.JobId).NotEmpty();
        RuleFor(p => p.Norm).InclusiveBetween(1, Position.MaxNorm);
        RuleFor(p => p.Status).Cascade(CascadeMode.Stop).NotNull().IsInEnum();
    }
}

internal static class PositionRules
{
    /// <summary>An active job of the catalog; an unchanged job may have been deactivated since.</summary>
    public static async Task<FieldError?> CheckJobAsync(IHrDbContext db, JobId jobId, JobId? current, CancellationToken cancellationToken)
    {
        if (jobId == current)
        {
            return null;
        }

        var job = await db.Jobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => new { j.IsActive }).SingleOrDefaultAsync(cancellationToken);
        return job switch
        {
            null => new FieldError("jobId", OrgErrors.UnknownJobCode),
            { IsActive: false } => new FieldError("jobId", OrgErrors.JobInactiveCode),
            _ => null,
        };
    }

    /// <summary>An open position of the same company other than the position itself.</summary>
    public static async Task<FieldError?> CheckReportsToAsync(
        IHrDbContext db,
        PositionId? reportsTo,
        Guid companyId,
        PositionId? self,
        CancellationToken cancellationToken)
    {
        if (reportsTo is not { } id)
        {
            return null;
        }

        var valid = id != self && await db.Positions.AsNoTracking()
            .AnyAsync(p => p.Id == id && p.CompanyId == companyId && p.Status != PositionStatus.Closed, cancellationToken);
        return valid ? null : new FieldError("reportsToPositionId", OrgErrors.InvalidReportsToCode);
    }
}

internal sealed class CreatePositionCommandHandler(IHrDbContext db, IAccessScopeProvider scopes, IOrganizationLookup organization)
    : ICommandHandler<CreatePositionCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreatePositionCommand request, CancellationToken cancellationToken)
    {
        var unitId = OrgUnitId.From(request.OrgUnitId);
        var unit = await db.OrgUnits.AsNoTracking().Where(u => u.Id == unitId).Select(u => new { u.CompanyId }).SingleOrDefaultAsync(cancellationToken);
        if (unit is null)
        {
            return Error.Validation([new FieldError("orgUnitId", OrgErrors.UnknownOrgUnitCode)]);
        }

        var scope = await OrgScope.ForCompaniesAsync(scopes, request.Permission, cancellationToken);
        if (!scope.CoversCompany(unit.CompanyId))
        {
            return CommonErrors.Forbidden;
        }

        if (await OrgReferences.CheckCompanyAsync(organization, unit.CompanyId, cancellationToken) is { } companyError)
        {
            return Error.Validation([companyError with { Field = "orgUnitId" }]);
        }

        var code = Code.Create(request.Code, Position.CodeMaxLength).Value;
        var reportsTo = request.ReportsToPositionId is { } managerId ? PositionId.From(managerId) : (PositionId?)null;
        var errors = new List<FieldError>();
        if (await db.Positions.AnyAsync(p => p.CompanyId == unit.CompanyId && p.Code == code.Value, cancellationToken))
        {
            errors.Add(OrgErrors.Duplicate("code"));
        }

        var jobId = JobId.From(request.JobId);
        errors.AddRange(new[]
        {
            await PositionRules.CheckJobAsync(db, jobId, null, cancellationToken),
            await PositionRules.CheckReportsToAsync(db, reportsTo, unit.CompanyId, null, cancellationToken),
        }.OfType<FieldError>());
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var position = Position.Create(
            unit.CompanyId,
            code,
            new PositionProfile(unitId, jobId, reportsTo, request.Norm ?? 1, PositionStatus.Active, request.IsManager));
        db.Positions.Add(position);
        if (await OrgChartLoader.HasCycleAsync(db, unit.CompanyId, cancellationToken))
        {
            return Error.Validation([new FieldError("reportsToPositionId", OrgErrors.ReportingCycleCode)]);
        }

        return position.Id.Value;
    }
}

internal sealed class UpdatePositionCommandHandler(IHrDbContext db, IAccessScopeProvider scopes) : ICommandHandler<UpdatePositionCommand>
{
    public async Task<Result> Handle(UpdatePositionCommand request, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ForCompaniesAsync(scopes, request.Permission, cancellationToken);
        var position = await db.Positions.SingleOrDefaultAsync(p => p.Id == PositionId.From(request.Id), cancellationToken);
        if (position is null || !scope.CoversCompany(position.CompanyId))
        {
            // Outside the caller's scope looks the same as missing (docs/domain/yetki-ve-onay.md §1.3)
            return CommonErrors.NotFound;
        }

        var unitId = OrgUnitId.From(request.OrgUnitId);
        var jobId = JobId.From(request.JobId);
        var reportsTo = request.ReportsToPositionId is { } managerId ? PositionId.From(managerId) : (PositionId?)null;
        var status = request.Status!.Value;
        var errors = new List<FieldError>();
        if (unitId != position.OrgUnitId)
        {
            var unitCompany = await db.OrgUnits.AsNoTracking().Where(u => u.Id == unitId).Select(u => (Guid?)u.CompanyId).SingleOrDefaultAsync(cancellationToken);
            if (unitCompany is null)
            {
                errors.Add(new FieldError("orgUnitId", OrgErrors.UnknownOrgUnitCode));
            }
            else if (unitCompany != position.CompanyId)
            {
                errors.Add(new FieldError("orgUnitId", OrgErrors.OrgUnitInOtherCompanyCode));
            }
        }

        errors.AddRange(new[]
        {
            await PositionRules.CheckJobAsync(db, jobId, position.JobId, cancellationToken),
            reportsTo == position.ReportsToPositionId
                ? null
                : await PositionRules.CheckReportsToAsync(db, reportsTo, position.CompanyId, position.Id, cancellationToken),
        }.OfType<FieldError>());

        // The unit's manager pointer must stay on an open position of the unit
        if ((status == PositionStatus.Closed || unitId != position.OrgUnitId)
            && await db.OrgUnits.AnyAsync(u => u.ManagerPositionId == position.Id, cancellationToken))
        {
            errors.Add(new FieldError(status == PositionStatus.Closed ? "status" : "orgUnitId", OrgErrors.ManagesUnitCode));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var structural = unitId != position.OrgUnitId || reportsTo != position.ReportsToPositionId || status != position.Status;
        db.Entry(position).ExpectVersion(request.Version);
        position.Update(new PositionProfile(unitId, jobId, reportsTo, request.Norm, status, request.IsManager));
        if (structural && await OrgChartLoader.HasCycleAsync(db, position.CompanyId, cancellationToken))
        {
            return Error.Validation([new FieldError("reportsToPositionId", OrgErrors.ReportingCycleCode)]);
        }

        return Result.Success();
    }
}
