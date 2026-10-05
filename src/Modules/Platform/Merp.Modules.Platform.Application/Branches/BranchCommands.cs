using FluentValidation;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Persistence;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Validation;
using Merp.Modules.Platform.Application.Common;
using Merp.Modules.Platform.Application.Locations;
using Merp.Modules.Platform.Contracts.Companies;
using Merp.Modules.Platform.Domain.Branches;
using Merp.Modules.Platform.Domain.Companies;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Branches;

public interface IBranchFields
{
    string Name { get; }

    BranchType Type { get; }

    string AddressLine1 { get; }

    string? AddressLine2 { get; }

    int ProvinceCode { get; }

    int DistrictId { get; }

    string? PostalCode { get; }

    string? SgkWorkplaceNo { get; }
}

public sealed record CreateBranchCommand(
    Guid CompanyId,
    string Code,
    string Name,
    BranchType Type,
    string AddressLine1,
    string? AddressLine2,
    int ProvinceCode,
    int DistrictId,
    string? PostalCode,
    string? SgkWorkplaceNo) : ICommand<Guid>, IBranchFields, IRequirePermission
{
    public string Permission => PlatformPermissions.Branches.Manage;
}

public sealed record UpdateBranchCommand(
    Guid Id,
    uint Version,
    string Name,
    BranchType Type,
    string AddressLine1,
    string? AddressLine2,
    int ProvinceCode,
    int DistrictId,
    string? PostalCode,
    string? SgkWorkplaceNo,
    bool IsActive) : ICommand, IBranchFields, IRequirePermission
{
    public string Permission => PlatformPermissions.Branches.Manage;
}

/// <summary>Field rules shared by create and update; an extension so the validator scan does not pick it up.</summary>
internal static class BranchFieldRules
{
    public static void ValidateBranchFields<T>(this AbstractValidator<T> v)
        where T : IBranchFields
    {
        v.RuleFor(b => b.Name).NotEmpty().MaximumLength(Branch.NameMaxLength);
        v.RuleFor(b => b.Type).IsInEnum();
        v.RuleFor(b => b.AddressLine1).NotEmpty().MaximumLength(Address.LineMaxLength);
        v.RuleFor(b => b.AddressLine2).MaximumLength(Address.LineMaxLength);
        v.RuleFor(b => b.ProvinceCode).GreaterThan(0);
        v.RuleFor(b => b.DistrictId).GreaterThan(0);
        v.RuleFor(b => b.PostalCode)
            .Matches("^[0-9]{5}$").WithErrorCode(CommonErrors.InvalidPostalCode.Code)
            .When(b => !string.IsNullOrWhiteSpace(b.PostalCode));
        v.RuleFor(b => b.SgkWorkplaceNo).MustBeValid(SgkWorkplaceNo.Create).When(b => !string.IsNullOrWhiteSpace(b.SgkWorkplaceNo));
    }
}

internal sealed class CreateBranchCommandValidator : AbstractValidator<CreateBranchCommand>
{
    public CreateBranchCommandValidator()
    {
        RuleFor(b => b.CompanyId).NotEmpty();
        RuleFor(b => b.Code)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(Branch.CodeMaxLength)
            .Matches("^[A-Za-z0-9][A-Za-z0-9_-]*$").WithErrorCode(CommonErrors.InvalidCode.Code);
        this.ValidateBranchFields();
    }
}

internal sealed class UpdateBranchCommandValidator : AbstractValidator<UpdateBranchCommand>
{
    public UpdateBranchCommandValidator() => this.ValidateBranchFields();
}

internal sealed class CreateBranchCommandHandler(
    IPlatformDbContext db,
    PlatformAccess access,
    LocationResolver locations,
    IIntegrationEventPublisher events) : ICommandHandler<CreateBranchCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateBranchCommand request, CancellationToken cancellationToken)
    {
        var companyId = CompanyId.From(request.CompanyId);
        var scope = await access.GetAsync(request.Permission, cancellationToken);
        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);

        // A company outside the caller's scope is reported like a missing one.
        if (company is null || !scope.CoversCompany(companyId))
        {
            return PlatformErrors.UnknownCompany;
        }

        if (!company.IsActive)
        {
            return PlatformErrors.InactiveCompany;
        }

        var code = Company.NormalizeCode(request.Code);
        if (await db.Branches.AnyAsync(b => b.CompanyId == companyId && b.Code == code, cancellationToken))
        {
            return PlatformErrors.BranchCodeTaken;
        }

        var location = await locations.ResolveAsync(
            request.ProvinceCode, request.DistrictId, request.AddressLine1, request.AddressLine2, request.PostalCode, cancellationToken);
        if (location.IsFailure)
        {
            return location.Error;
        }

        var branch = Branch.Create(companyId, code, Details(request, location.Value));
        db.Branches.Add(branch);
        await events.PublishAsync(
            new BranchCreated(branch.Id.Value, branch.Code, branch.Name, branch.Type.ToString(), branch.IsActive) { CompanyId = companyId.Value },
            cancellationToken);
        return branch.Id.Value;
    }

    internal static BranchDetails Details(IBranchFields f, Location location) => new(
        f.Name,
        f.Type,
        location,
        string.IsNullOrWhiteSpace(f.SgkWorkplaceNo) ? null : SgkWorkplaceNo.Create(f.SgkWorkplaceNo).Value);
}

internal sealed class UpdateBranchCommandHandler(
    IPlatformDbContext db,
    PlatformAccess access,
    LocationResolver locations,
    IIntegrationEventPublisher events) : ICommandHandler<UpdateBranchCommand>
{
    public async Task<Result> Handle(UpdateBranchCommand request, CancellationToken cancellationToken)
    {
        var id = BranchId.From(request.Id);
        var scope = await access.GetAsync(request.Permission, cancellationToken);
        var branch = await db.Branches.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (branch is null || !scope.CoversBranch(branch))
        {
            return CommonErrors.NotFound;
        }

        var location = await locations.ResolveAsync(
            request.ProvinceCode, request.DistrictId, request.AddressLine1, request.AddressLine2, request.PostalCode, cancellationToken);
        if (location.IsFailure)
        {
            return location.Error;
        }

        db.Entry(branch).Property(ShadowProperties.Version).OriginalValue = request.Version;
        branch.Update(CreateBranchCommandHandler.Details(request, location.Value), request.IsActive);
        await events.PublishAsync(
            new BranchUpdated(branch.Id.Value, branch.Code, branch.Name, branch.Type.ToString(), branch.IsActive) { CompanyId = branch.CompanyId.Value },
            cancellationToken);
        return Result.Success();
    }
}
