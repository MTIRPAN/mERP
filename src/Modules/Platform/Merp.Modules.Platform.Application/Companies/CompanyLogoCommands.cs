using FluentValidation;
using Merp.Application.Abstractions.Files;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence.Files;
using Merp.Modules.Platform.Application.Files;
using Merp.Modules.Platform.Application.Organization;
using Merp.Modules.Platform.Domain.Companies;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Companies;

/// <summary>
/// Sets or replaces a company's logo (PNG, JPEG or WebP, at most 1 MB) and returns the stored file's id. The logo is
/// a file of the company (purpose <c>logo</c>), not a field of the company record, so changing it does not change the
/// record's version while its form is open.
/// </summary>
public sealed record SetCompanyLogoCommand(Guid CompanyId, FileUpload? File) : ICommand<Guid>, IRequirePermission
{
    public const long MaxBytes = 1024 * 1024;

    public string Permission => PlatformPermissions.Companies.Manage;
}

/// <summary>Removes a company's logo.</summary>
public sealed record RemoveCompanyLogoCommand(Guid CompanyId) : ICommand, IRequirePermission
{
    public string Permission => PlatformPermissions.Companies.Manage;
}

internal sealed class SetCompanyLogoCommandValidator : AbstractValidator<SetCompanyLogoCommand>
{
    public SetCompanyLogoCommandValidator() => RuleFor(c => c.File).NotNull().OverridePropertyName(FileErrors.Field);
}

internal static class CompanyLogo
{
    /// <summary>True when the company exists and is in the caller's manage scope.</summary>
    public static async Task<bool> IsManagedAsync(IPlatformDbContext db, IAccessScopeProvider scopes, Guid companyId, CancellationToken cancellationToken)
    {
        var id = CompanyId.From(companyId);
        var scope = OrganizationScope.From(await scopes.GetCurrentUserScopesAsync(PlatformPermissions.Companies.Manage, cancellationToken));
        return scope.CoversCompany(id) && await db.Companies.AnyAsync(c => c.Id == id, cancellationToken);
    }

    /// <summary>Current logo files of the company; normally one.</summary>
    public static IQueryable<StoredFile> Files(IPlatformDbContext db, Guid companyId) =>
        db.StoredFiles.Where(f => f.OwnerType == PlatformFileOwners.Company && f.OwnerId == companyId && f.Purpose == StoredFilePurposes.Logo);

    /// <summary>Deletes the current logo files; their content goes after the commit.</summary>
    public static async Task RemoveAsync(IPlatformDbContext db, IFileStore files, Guid companyId, CancellationToken cancellationToken)
    {
        foreach (var file in await Files(db, companyId).ToListAsync(cancellationToken))
        {
            db.StoredFiles.Remove(file);
            files.PurgeAfterCommit(file);
        }
    }
}

internal sealed class SetCompanyLogoCommandHandler(IPlatformDbContext db, IAccessScopeProvider scopes, IFileStore files)
    : ICommandHandler<SetCompanyLogoCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SetCompanyLogoCommand request, CancellationToken cancellationToken)
    {
        if (!await CompanyLogo.IsManagedAsync(db, scopes, request.CompanyId, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        var stored = await files.SaveAsync(
            PlatformFileOwners.Company,
            request.CompanyId,
            StoredFilePurposes.Logo,
            request.File!,
            FileRules.Images(SetCompanyLogoCommand.MaxBytes),
            cancellationToken);
        if (stored.IsFailure)
        {
            return stored.Error;
        }

        await CompanyLogo.RemoveAsync(db, files, request.CompanyId, cancellationToken);
        db.StoredFiles.Add(stored.Value);
        return stored.Value.Id;
    }
}

internal sealed class RemoveCompanyLogoCommandHandler(IPlatformDbContext db, IAccessScopeProvider scopes, IFileStore files)
    : ICommandHandler<RemoveCompanyLogoCommand>
{
    public async Task<Result> Handle(RemoveCompanyLogoCommand request, CancellationToken cancellationToken)
    {
        if (!await CompanyLogo.IsManagedAsync(db, scopes, request.CompanyId, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        await CompanyLogo.RemoveAsync(db, files, request.CompanyId, cancellationToken);
        return Result.Success();
    }
}
