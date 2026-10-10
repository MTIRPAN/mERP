using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Persistence;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Validation;
using Merp.Modules.Platform.Application.Organization;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.Modules.Platform.Application.Calendars;
using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Domain.Calendars;
using Merp.Modules.Platform.Domain.Companies;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Branches;

/// <summary>
/// Creates a branch of a company. Needs <c>platform.branches.manage</c> with the Tenant scope or the company's
/// scope.
/// </summary>
public sealed record CreateBranchCommand(
    Guid CompanyId,
    string? Code,
    string? Name,
    BranchType? Type,
    AddressInput? Address,
    string? SgkWorkplaceNumber,
    string? Phone,
    Guid? WorkScheduleId = null) : ICommand<Guid>, IRequirePermission, IBranchFields
{
    [JsonIgnore]
    public string Permission => PlatformPermissions.Branches.Manage;
}

/// <summary>Updates a branch's details and active flag. The company and the code never change.</summary>
/// <remarks>Version is the one the client read; a concurrent change ends in 409.</remarks>
public sealed record UpdateBranchCommand(
    uint Version,
    string? Name,
    BranchType? Type,
    AddressInput? Address,
    string? SgkWorkplaceNumber,
    string? Phone,
    bool IsActive,
    Guid? WorkScheduleId = null) : ICommand, IRequirePermission, IBranchFields
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => PlatformPermissions.Branches.Manage;
}

/// <summary>Fields shared by create and update, validated by one rule set.</summary>
public interface IBranchFields
{
    string? Name { get; }

    BranchType? Type { get; }

    AddressInput? Address { get; }

    string? SgkWorkplaceNumber { get; }

    string? Phone { get; }

    /// <summary>A work schedule of the tenant or of the branch's company; null follows the company's default (PLAN P9).</summary>
    Guid? WorkScheduleId { get; }
}

internal static class BranchRules
{
    public static void AddFieldRules<T>(AbstractValidator<T> validator)
        where T : IBranchFields
    {
        validator.RuleFor(b => b.Name).NotEmpty().MaximumLength(Branch.NameMaxLength);
        validator.RuleFor(b => b.Type).Cascade(CascadeMode.Stop).NotNull().IsInEnum();
        validator.RuleFor(b => b.Address).NotNull();
        validator.RuleFor(b => b.SgkWorkplaceNumber).MustBeValid(SgkWorkplaceNumber.Create).When(b => !string.IsNullOrWhiteSpace(b.SgkWorkplaceNumber));
        validator.RuleFor(b => b.Phone).MustBeValid(PhoneNumber.Create).When(b => !string.IsNullOrWhiteSpace(b.Phone));
    }

    /// <summary>Address, and the one-head-office-per-company rule (the Type field).</summary>
    public static async Task<Result<BranchProfile>> ToProfileAsync(
        IPlatformDbContext db,
        AddressResolver addressResolver,
        IBranchFields fields,
        CompanyId companyId,
        BranchId? except,
        CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        if (fields.Type == BranchType.HeadOffice)
        {
            var headOffices = db.Branches.Where(b => b.CompanyId == companyId && b.Type == BranchType.HeadOffice);
            if (except is { } self)
            {
                headOffices = headOffices.Where(b => b.Id != self);
            }

            if (await headOffices.AnyAsync(cancellationToken))
            {
                errors.Add(new FieldError("type", OrganizationErrors.HeadOfficeExistsCode));
            }
        }

        var address = await addressResolver.ResolveAsync(fields.Address!, cancellationToken);
        if (address.IsFailure)
        {
            errors.AddRange(address.Error.Fields);
        }

        if (fields.WorkScheduleId is { } scheduleId)
        {
            var usable = await db.WorkSchedules.AnyAsync(
                s => s.Id == WorkScheduleId.From(scheduleId) && s.IsActive && (s.CompanyId == null || s.CompanyId == companyId),
                cancellationToken);
            if (!usable)
            {
                errors.Add(new FieldError("workScheduleId", CalendarRequests.UnknownScheduleCode));
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        return new BranchProfile(
            fields.Name!,
            fields.Type!.Value,
            address.Value,
            string.IsNullOrWhiteSpace(fields.SgkWorkplaceNumber) ? null : SgkWorkplaceNumber.Create(fields.SgkWorkplaceNumber).Value,
            string.IsNullOrWhiteSpace(fields.Phone) ? null : PhoneNumber.Create(fields.Phone).Value,
            fields.WorkScheduleId is { } id ? WorkScheduleId.From(id) : null);
    }

    public static BranchCreated ToCreatedEvent(Branch branch) =>
        new BranchCreated(branch.Id.Value, branch.Code, branch.Name, branch.Type.ToString(), branch.Address.ProvinceCode, branch.IsActive)
        {
            CompanyId = branch.CompanyId.Value,
        };

    public static BranchUpdated ToUpdatedEvent(Branch branch) =>
        new BranchUpdated(branch.Id.Value, branch.Code, branch.Name, branch.Type.ToString(), branch.Address.ProvinceCode, branch.IsActive)
        {
            CompanyId = branch.CompanyId.Value,
        };
}

internal sealed class CreateBranchCommandValidator : AbstractValidator<CreateBranchCommand>
{
    public CreateBranchCommandValidator()
    {
        RuleFor(b => b.CompanyId).NotEmpty();
        RuleFor(b => b.Code).Cascade(CascadeMode.Stop).NotEmpty().MustBeValid(v => Code.Create(v, Branch.CodeMaxLength));
        BranchRules.AddFieldRules(this);
    }
}

internal sealed class UpdateBranchCommandValidator : AbstractValidator<UpdateBranchCommand>
{
    public UpdateBranchCommandValidator() => BranchRules.AddFieldRules(this);
}

internal sealed class CreateBranchCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    AddressResolver addressResolver,
    IIntegrationEventPublisher publisher,
    ICalendarCache calendarCache,
    IAfterCommit afterCommit) : ICommandHandler<CreateBranchCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateBranchCommand request, CancellationToken cancellationToken)
    {
        var companyId = CompanyId.From(request.CompanyId);
        var scope = OrganizationScope.From(await scopes.GetCurrentUserScopesAsync(request.Permission, cancellationToken));
        if (!scope.CoversCompany(companyId))
        {
            return CommonErrors.Forbidden;
        }

        var company = await db.Companies.Where(c => c.Id == companyId).Select(c => new { c.IsActive }).SingleOrDefaultAsync(cancellationToken);
        if (company is null)
        {
            return Error.Validation([new FieldError("companyId", OrganizationErrors.UnknownCompanyCode)]);
        }

        if (!company.IsActive)
        {
            return Error.Validation([new FieldError("companyId", OrganizationErrors.CompanyInactiveCode)]);
        }

        var code = Code.Create(request.Code, Branch.CodeMaxLength).Value;
        var errors = new List<FieldError>();
        if (await db.Branches.AnyAsync(b => b.CompanyId == companyId && b.Code == code.Value, cancellationToken))
        {
            errors.Add(OrganizationErrors.Duplicate("code"));
        }

        var profile = await BranchRules.ToProfileAsync(db, addressResolver, request, companyId, null, cancellationToken);
        if (profile.IsFailure)
        {
            errors.AddRange(profile.Error.Fields);
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var branch = Branch.Create(companyId, code, profile.Value);
        db.Branches.Add(branch);
        await publisher.PublishAsync(BranchRules.ToCreatedEvent(branch), cancellationToken);
        // The branch's schedule takes part in resolving working days (PLAN P9)
        afterCommit.Enqueue(calendarCache.InvalidateAsync);
        return branch.Id.Value;
    }
}

internal sealed class UpdateBranchCommandHandler(
    IPlatformDbContext db,
    IAccessScopeProvider scopes,
    AddressResolver addressResolver,
    IIntegrationEventPublisher publisher,
    ICalendarCache calendarCache,
    IAfterCommit afterCommit) : ICommandHandler<UpdateBranchCommand>
{
    public async Task<Result> Handle(UpdateBranchCommand request, CancellationToken cancellationToken)
    {
        var id = BranchId.From(request.Id);
        var scope = OrganizationScope.From(await scopes.GetCurrentUserScopesAsync(request.Permission, cancellationToken));
        var branch = await db.Branches.SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (branch is null || !scope.CoversBranch(branch))
        {
            // Outside the caller's scope looks the same as missing (docs/domain/yetki-ve-onay.md §1.3)
            return CommonErrors.NotFound;
        }

        var profile = await BranchRules.ToProfileAsync(db, addressResolver, request, branch.CompanyId, branch.Id, cancellationToken);
        if (profile.IsFailure)
        {
            return profile.Error;
        }

        db.Entry(branch).ExpectVersion(request.Version);
        branch.Update(profile.Value);
        if (request.IsActive)
        {
            branch.Activate();
        }
        else
        {
            branch.Deactivate();
        }

        await publisher.PublishAsync(BranchRules.ToUpdatedEvent(branch), cancellationToken);
        afterCommit.Enqueue(calendarCache.InvalidateAsync);
        return Result.Success();
    }
}
