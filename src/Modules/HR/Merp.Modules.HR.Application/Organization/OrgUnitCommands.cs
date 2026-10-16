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
/// Creates a unit of a company, at the top or under a parent unit of the same company. Needs
/// <c>hr.org.manage</c> with the Tenant scope or the company's scope (D37).
/// </summary>
public sealed record CreateOrgUnitCommand(
    Guid CompanyId,
    Guid? ParentId,
    string? Code,
    string? Name,
    OrgUnitType? Type,
    string? CostCenterCode = null,
    Guid? BranchId = null,
    DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null) : ICommand<Guid>, IRequirePermission, IOrgUnitFields
{
    [JsonIgnore]
    public string Permission => HrPermissions.Org.Manage;
}

/// <summary>
/// Updates a unit's details and its manager position, a position of the unit. The company, the code and the parent
/// do not change here; moving has its own command.
/// </summary>
/// <remarks>Version is the one the client read; a concurrent change ends in 409.</remarks>
public sealed record UpdateOrgUnitCommand(
    uint Version,
    string? Name,
    OrgUnitType? Type,
    Guid? ManagerPositionId = null,
    string? CostCenterCode = null,
    Guid? BranchId = null,
    DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null) : ICommand, IRequirePermission, IOrgUnitFields
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Org.Manage;
}

/// <summary>
/// Moves a unit under another unit of the same company, or to the top without a parent. The sub units move with it
/// (D37). A unit cannot move under itself or one of its sub units.
/// </summary>
public sealed record MoveOrgUnitCommand(uint Version, Guid? ParentId) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Org.Manage;
}

/// <summary>Fields shared by create and update, validated by one rule set.</summary>
public interface IOrgUnitFields
{
    string? Name { get; }

    OrgUnitType? Type { get; }

    string? CostCenterCode { get; }

    /// <summary>A branch of the unit's company.</summary>
    Guid? BranchId { get; }

    DateOnly? ValidFrom { get; }

    DateOnly? ValidTo { get; }
}

internal static class OrgUnitRules
{
    public static void AddFieldRules<T>(AbstractValidator<T> validator)
        where T : IOrgUnitFields
    {
        validator.RuleFor(u => u.Name).NotEmpty().MaximumLength(OrgUnit.NameMaxLength);
        validator.RuleFor(u => u.Type).Cascade(CascadeMode.Stop).NotNull().IsInEnum();
        validator.RuleFor(u => u.CostCenterCode).MaximumLength(OrgUnit.CostCenterCodeMaxLength);
        validator.RuleFor(u => u.ValidTo)
            .GreaterThanOrEqualTo(u => u.ValidFrom)
            .WithErrorCode(CommonErrors.InvalidDateRange.Code)
            .When(u => u.ValidFrom is not null && u.ValidTo is not null);
    }

    public static OrgUnitProfile ToProfile(IOrgUnitFields fields) =>
        new(fields.Name!, fields.Type!.Value, fields.CostCenterCode, fields.BranchId, fields.ValidFrom, fields.ValidTo);
}

internal sealed class CreateOrgUnitCommandValidator : AbstractValidator<CreateOrgUnitCommand>
{
    public CreateOrgUnitCommandValidator()
    {
        RuleFor(u => u.CompanyId).NotEmpty();
        RuleFor(u => u.Code).Cascade(CascadeMode.Stop).NotEmpty().MustBeValid(v => Code.Create(v, OrgUnit.CodeMaxLength));
        OrgUnitRules.AddFieldRules(this);
    }
}

internal sealed class UpdateOrgUnitCommandValidator : AbstractValidator<UpdateOrgUnitCommand>
{
    public UpdateOrgUnitCommandValidator() => OrgUnitRules.AddFieldRules(this);
}

internal sealed class CreateOrgUnitCommandHandler(IHrDbContext db, IAccessScopeProvider scopes, IOrganizationLookup organization)
    : ICommandHandler<CreateOrgUnitCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateOrgUnitCommand request, CancellationToken cancellationToken)
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

        var code = Code.Create(request.Code, OrgUnit.CodeMaxLength).Value;
        var errors = new List<FieldError>();
        if (await db.OrgUnits.AnyAsync(u => u.CompanyId == request.CompanyId && u.Code == code.Value, cancellationToken))
        {
            errors.Add(OrgErrors.Duplicate("code"));
        }

        OrgUnit? parent = null;
        if (request.ParentId is { } parentId)
        {
            parent = await db.OrgUnits.AsNoTracking().SingleOrDefaultAsync(u => u.Id == OrgUnitId.From(parentId), cancellationToken);
            if (parent is null || parent.CompanyId != request.CompanyId)
            {
                errors.Add(new FieldError("parentId", OrgErrors.UnknownParentCode));
            }
        }

        if (await OrgReferences.CheckBranchAsync(organization, request.BranchId, request.CompanyId, cancellationToken) is { } branchError)
        {
            errors.Add(branchError);
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var unit = OrgUnit.Create(request.CompanyId, parent, code, OrgUnitRules.ToProfile(request));
        db.OrgUnits.Add(unit);
        return unit.Id.Value;
    }
}

internal sealed class UpdateOrgUnitCommandHandler(IHrDbContext db, IAccessScopeProvider scopes, IOrganizationLookup organization)
    : ICommandHandler<UpdateOrgUnitCommand>
{
    public async Task<Result> Handle(UpdateOrgUnitCommand request, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ForCompaniesAsync(scopes, request.Permission, cancellationToken);
        var unit = await db.OrgUnits.SingleOrDefaultAsync(u => u.Id == OrgUnitId.From(request.Id), cancellationToken);
        if (unit is null || !scope.CoversCompany(unit.CompanyId))
        {
            // Outside the caller's scope looks the same as missing (docs/domain/yetki-ve-onay.md §1.3)
            return CommonErrors.NotFound;
        }

        var errors = new List<FieldError>();
        if (request.BranchId != unit.BranchId
            && await OrgReferences.CheckBranchAsync(organization, request.BranchId, unit.CompanyId, cancellationToken) is { } branchError)
        {
            errors.Add(branchError);
        }

        var managerId = request.ManagerPositionId is { } id ? PositionId.From(id) : (PositionId?)null;
        if (managerId is { } newManager && newManager != unit.ManagerPositionId)
        {
            var manager = await db.Positions.AsNoTracking()
                .Where(p => p.Id == newManager)
                .Select(p => new { p.OrgUnitId, p.Status })
                .SingleOrDefaultAsync(cancellationToken);
            if (manager is null || manager.OrgUnitId != unit.Id || manager.Status == PositionStatus.Closed)
            {
                errors.Add(new FieldError("managerPositionId", OrgErrors.InvalidManagerPositionCode));
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(unit).ExpectVersion(request.Version);
        unit.Update(OrgUnitRules.ToProfile(request));
        var managerChanged = managerId != unit.ManagerPositionId;
        unit.ChangeManager(managerId);
        if (managerChanged && await OrgChartLoader.HasCycleAsync(db, unit.CompanyId, cancellationToken))
        {
            return Error.Validation([new FieldError("managerPositionId", OrgErrors.ReportingCycleCode)]);
        }

        return Result.Success();
    }
}

internal sealed class MoveOrgUnitCommandHandler(IHrDbContext db, IAccessScopeProvider scopes) : ICommandHandler<MoveOrgUnitCommand>
{
    public async Task<Result> Handle(MoveOrgUnitCommand request, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ForCompaniesAsync(scopes, request.Permission, cancellationToken);
        var unit = await db.OrgUnits.SingleOrDefaultAsync(u => u.Id == OrgUnitId.From(request.Id), cancellationToken);
        if (unit is null || !scope.CoversCompany(unit.CompanyId))
        {
            return CommonErrors.NotFound;
        }

        OrgUnit? parent = null;
        if (request.ParentId is { } parentId)
        {
            parent = await db.OrgUnits.AsNoTracking().SingleOrDefaultAsync(u => u.Id == OrgUnitId.From(parentId), cancellationToken);
            if (parent is null || parent.CompanyId != unit.CompanyId)
            {
                return Error.Validation([new FieldError("parentId", OrgErrors.UnknownParentCode)]);
            }
        }

        db.Entry(unit).ExpectVersion(request.Version);
        var previousParent = unit.ParentId;
        var moved = unit.MoveTo(parent);
        if (moved.IsFailure)
        {
            return moved;
        }

        // The unit's manager may now report into its own sub tree through the new parent's manager
        if (unit.ParentId != previousParent && await OrgChartLoader.HasCycleAsync(db, unit.CompanyId, cancellationToken))
        {
            return Error.Validation([new FieldError("parentId", OrgErrors.ReportingCycleCode)]);
        }

        return Result.Success();
    }
}
