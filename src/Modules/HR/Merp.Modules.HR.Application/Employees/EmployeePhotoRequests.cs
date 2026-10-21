using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Files;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence.Files;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Employees;

/// <summary>HR records that carry files (D31); their metadata is in the <c>hr</c> schema.</summary>
public static class HrFileOwners
{
    public const string Employee = "hr.employee";
}

/// <summary>Content of a stored file; the caller disposes <see cref="Content"/>.</summary>
public sealed record HrFileDownload(string FileName, string ContentType, long Length, Stream Content);

/// <summary>
/// Sets or replaces a person's photo (PNG, JPEG or WebP, at most 2 MB) and returns the stored file's id. Like a
/// company logo the photo is a file of the record (purpose <c>photo</c>), so changing it does not change the card's
/// version while its form is open (D31, D38).
/// </summary>
public sealed record SetEmployeePhotoCommand(Guid EmployeeId, FileUpload? File) : ICommand<Guid>, IRequirePermission
{
    public const long MaxBytes = 2 * 1024 * 1024;

    public string Permission => HrPermissions.Employees.Update;
}

/// <summary>Removes a person's photo.</summary>
public sealed record RemoveEmployeePhotoCommand(Guid EmployeeId) : ICommand, IRequirePermission
{
    public string Permission => HrPermissions.Employees.Update;
}

/// <summary>A person's photo for display; 404 when there is none or the person is outside the caller's scope.</summary>
public sealed record GetEmployeePhotoQuery(Guid EmployeeId) : IQuery<HrFileDownload>, IRequirePermission
{
    public string Permission => HrPermissions.Employees.Read;
}

internal sealed class SetEmployeePhotoCommandValidator : AbstractValidator<SetEmployeePhotoCommand>
{
    public SetEmployeePhotoCommandValidator() => RuleFor(c => c.File).NotNull().OverridePropertyName(FileErrors.Field);
}

internal static class EmployeePhoto
{
    /// <summary>True when the person exists and works or worked in a company of the caller's update scope.</summary>
    public static async Task<bool> IsManagedAsync(IHrDbContext db, IAccessScopeProvider scopes, Guid employeeId, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ForCompaniesAsync(scopes, HrPermissions.Employees.Update, cancellationToken);
        var id = EmployeeId.From(employeeId);
        var companies = await db.Employees.AsNoTracking()
            .Where(e => e.Id == id)
            .SelectMany(e => e.Employments.Select(m => m.CompanyId))
            .ToListAsync(cancellationToken);
        return companies.Any(scope.CoversCompany);
    }

    /// <summary>Deletes the current photo files; their content goes after the commit.</summary>
    public static async Task RemoveAsync(IHrDbContext db, IFileStore files, Guid employeeId, CancellationToken cancellationToken)
    {
        foreach (var file in await EmployeePhotos.Files(db, [employeeId]).ToListAsync(cancellationToken))
        {
            db.StoredFiles.Remove(file);
            files.PurgeAfterCommit(file);
        }
    }
}

internal sealed class SetEmployeePhotoCommandHandler(IHrDbContext db, IAccessScopeProvider scopes, IFileStore files)
    : ICommandHandler<SetEmployeePhotoCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SetEmployeePhotoCommand request, CancellationToken cancellationToken)
    {
        if (!await EmployeePhoto.IsManagedAsync(db, scopes, request.EmployeeId, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        var stored = await files.SaveAsync(
            HrFileOwners.Employee,
            request.EmployeeId,
            StoredFilePurposes.Photo,
            request.File!,
            FileRules.Images(SetEmployeePhotoCommand.MaxBytes),
            cancellationToken);
        if (stored.IsFailure)
        {
            return stored.Error;
        }

        await EmployeePhoto.RemoveAsync(db, files, request.EmployeeId, cancellationToken);
        db.StoredFiles.Add(stored.Value);
        return stored.Value.Id;
    }
}

internal sealed class RemoveEmployeePhotoCommandHandler(IHrDbContext db, IAccessScopeProvider scopes, IFileStore files)
    : ICommandHandler<RemoveEmployeePhotoCommand>
{
    public async Task<Result> Handle(RemoveEmployeePhotoCommand request, CancellationToken cancellationToken)
    {
        if (!await EmployeePhoto.IsManagedAsync(db, scopes, request.EmployeeId, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        await EmployeePhoto.RemoveAsync(db, files, request.EmployeeId, cancellationToken);
        return Result.Success();
    }
}

internal sealed class GetEmployeePhotoQueryHandler(
    IHrDbContext db,
    IAccessScopeProvider scopes,
    IPositionHolders holders,
    ICurrentUser currentUser,
    IOrganizationLookup organization,
    IFileStore files,
    TimeProvider timeProvider) : IQueryHandler<GetEmployeePhotoQuery, HrFileDownload>
{
    public async Task<Result<HrFileDownload>> Handle(GetEmployeePhotoQuery request, CancellationToken cancellationToken)
    {
        var id = EmployeeId.From(request.EmployeeId);
        var days = await CompanyDays.ResolveAsync(db, organization, timeProvider, cancellationToken);
        var scope = await EmployeeScope.ResolveAsync(scopes, request.Permission, db, holders, currentUser, cancellationToken);
        if (!await scope.Filter(EmploymentRows.Query(db.Employees.AsNoTracking().Where(e => e.Id == id), days), db).AnyAsync(cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        var file = await EmployeePhotos.Files(db, [request.EmployeeId]).AsNoTracking()
            .OrderByDescending(f => f.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (file is null)
        {
            return CommonErrors.NotFound;
        }

        var content = await files.OpenReadAsync(file, cancellationToken);
        return new HrFileDownload(file.FileName, file.ContentType, file.Length, content);
    }
}
