using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Files;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence;
using Merp.Application.Persistence.Files;
using Merp.Modules.HR.Application.Employees;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Domain.Documents;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Documents;

/// <summary>
/// The documents of a person in the caller's scope, by type, and whether the caller may change them (D38). Reading
/// needs <c>hr.employees.documents.read</c> on the person; changing <c>hr.employees.documents.manage</c> on one of the
/// person's companies.
/// </summary>
public sealed record GetEmployeeDocumentsQuery(Guid EmployeeId) : IQuery<EmployeeDocumentList>, IRequirePermission
{
    public string Permission => HrPermissions.Employees.Documents.Read;
}

public sealed record EmployeeDocumentList(IReadOnlyList<EmployeeDocumentItem> Items, bool CanManage, IReadOnlyList<string> Extensions, long MaxBytes);

/// <remarks>
/// DaysLeft counts the days until ValidUntil on the person's company day (negative once expired). The file is
/// downloaded from <c>/api/hr/employees/{employeeId}/documents/{id}/file</c>. Send Version back when updating.
/// </remarks>
public sealed record EmployeeDocumentItem(
    Guid Id,
    Guid TypeId,
    string TypeCode,
    string TypeName,
    string? Number,
    DateOnly? IssuedOn,
    DateOnly? ValidUntil,
    int? DaysLeft,
    string? Note,
    string? FileName,
    string? ContentType,
    long? Length,
    uint Version,
    DateTimeOffset CreatedAt);

/// <summary>Adds a document with its file; returns the document's id.</summary>
public sealed record AddEmployeeDocumentCommand(
    Guid EmployeeId,
    Guid? TypeId,
    string? Number,
    DateOnly? IssuedOn,
    DateOnly? ValidUntil,
    string? Note,
    FileUpload? File) : ICommand<Guid>, IRequirePermission, IEmployeeDocumentFields
{
    public string Permission => HrPermissions.Employees.Documents.Manage;
}

/// <summary>Changes a document's type, number, validity or note; a new validity date restarts its reminders.</summary>
public sealed record UpdateEmployeeDocumentCommand(
    uint Version,
    Guid? TypeId,
    string? Number,
    DateOnly? IssuedOn,
    DateOnly? ValidUntil,
    string? Note) : ICommand, IRequirePermission, IEmployeeDocumentFields
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid EmployeeId { get; init; }

    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid DocumentId { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Employees.Documents.Manage;
}

/// <summary>Replaces a document's file, e.g. with a better scan; the old content is removed after the commit.</summary>
public sealed record ReplaceEmployeeDocumentFileCommand(Guid EmployeeId, Guid DocumentId, FileUpload? File) : ICommand, IRequirePermission
{
    public string Permission => HrPermissions.Employees.Documents.Manage;
}

/// <summary>Deletes a document and its file; the record stays in the audit trail.</summary>
public sealed record DeleteEmployeeDocumentCommand(Guid EmployeeId, Guid DocumentId) : ICommand, IRequirePermission
{
    public string Permission => HrPermissions.Employees.Documents.Manage;
}

/// <summary>A document's file for download; 404 unless the caller sees the person's documents.</summary>
public sealed record GetEmployeeDocumentFileQuery(Guid EmployeeId, Guid DocumentId) : IQuery<HrFileDownload>, IRequirePermission
{
    public string Permission => HrPermissions.Employees.Documents.Read;
}

public interface IEmployeeDocumentFields
{
    Guid? TypeId { get; }

    string? Number { get; }

    string? Note { get; }
}

public static class EmployeeDocumentCommandErrors
{
    public const string UnknownTypeCode = "hr.documents.errors.unknown_type";
}

/// <summary>HR records that carry files (D31): a document's file belongs to the document.</summary>
public static class HrDocumentFiles
{
    public const string OwnerType = "hr.employee_document";
}

internal static class EmployeeDocumentRules
{
    public static void AddFieldRules<T>(AbstractValidator<T> validator)
        where T : IEmployeeDocumentFields
    {
        validator.RuleFor(d => d.TypeId).NotEmpty();
        validator.RuleFor(d => d.Number).MaximumLength(EmployeeDocument.NumberMaxLength);
        validator.RuleFor(d => d.Note).MaximumLength(EmployeeDocument.NoteMaxLength);
    }

    /// <summary>True when the person exists and works or worked in a company of the caller's manage scope.</summary>
    public static async Task<bool> IsManagedAsync(IHrDbContext db, IAccessScopeProvider scopes, Guid employeeId, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ForCompaniesAsync(scopes, HrPermissions.Employees.Documents.Manage, cancellationToken);
        var id = EmployeeId.From(employeeId);
        var companies = await db.Employees.AsNoTracking()
            .Where(e => e.Id == id)
            .SelectMany(e => e.Employments.Select(m => m.CompanyId))
            .ToListAsync(cancellationToken);
        return companies.Any(scope.CoversCompany);
    }

    /// <summary>True when the caller sees the person through the scopes of <c>hr.employees.documents.read</c>.</summary>
    public static async Task<bool> IsVisibleAsync(
        IHrDbContext db,
        IAccessScopeProvider scopes,
        IPositionHolders holders,
        ICurrentUser currentUser,
        CompanyDays days,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var id = EmployeeId.From(employeeId);
        var scope = await EmployeeScope.ResolveAsync(scopes, HrPermissions.Employees.Documents.Read, db, holders, currentUser, cancellationToken);
        return await scope.Filter(EmploymentRows.Query(db.Employees.AsNoTracking().Where(e => e.Id == id), days), db).AnyAsync(cancellationToken);
    }

    /// <summary>An active type, or the type the document already has.</summary>
    public static async Task<Result<DocumentDetails>> ResolveAsync(
        IHrDbContext db,
        IEmployeeDocumentFields fields,
        DateOnly? issuedOn,
        DateOnly? validUntil,
        DocumentTypeId? current,
        CancellationToken cancellationToken)
    {
        var typeId = DocumentTypeId.From(fields.TypeId!.Value);
        var usable = typeId == current || await db.DocumentTypes.AnyAsync(t => t.Id == typeId && t.IsActive, cancellationToken);
        return usable
            ? new DocumentDetails(typeId, Blank(fields.Number), issuedOn, validUntil, Blank(fields.Note))
            : Error.Validation([new FieldError("typeId", EmployeeDocumentCommandErrors.UnknownTypeCode)]);
    }

    public static Task<EmployeeDocument?> FindAsync(IHrDbContext db, Guid employeeId, Guid documentId, CancellationToken cancellationToken) =>
        db.EmployeeDocuments.SingleOrDefaultAsync(
            d => d.Id == EmployeeDocumentId.From(documentId) && d.EmployeeId == EmployeeId.From(employeeId),
            cancellationToken);

    public static IQueryable<StoredFile> Files(IHrDbContext db, IReadOnlyCollection<Guid> documentIds) =>
        db.StoredFiles.Where(f => f.OwnerType == HrDocumentFiles.OwnerType && documentIds.Contains(f.OwnerId) && f.Purpose == StoredFilePurposes.Attachment);

    /// <summary>Deletes the document's current files; their content goes after the commit.</summary>
    public static async Task RemoveFilesAsync(IHrDbContext db, IFileStore files, Guid documentId, CancellationToken cancellationToken)
    {
        foreach (var file in await Files(db, [documentId]).ToListAsync(cancellationToken))
        {
            db.StoredFiles.Remove(file);
            files.PurgeAfterCommit(file);
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class AddEmployeeDocumentCommandValidator : AbstractValidator<AddEmployeeDocumentCommand>
{
    public AddEmployeeDocumentCommandValidator()
    {
        EmployeeDocumentRules.AddFieldRules(this);
        RuleFor(c => c.File).NotNull().OverridePropertyName(FileErrors.Field);
    }
}

internal sealed class UpdateEmployeeDocumentCommandValidator : AbstractValidator<UpdateEmployeeDocumentCommand>
{
    public UpdateEmployeeDocumentCommandValidator() => EmployeeDocumentRules.AddFieldRules(this);
}

internal sealed class ReplaceEmployeeDocumentFileCommandValidator : AbstractValidator<ReplaceEmployeeDocumentFileCommand>
{
    public ReplaceEmployeeDocumentFileCommandValidator() => RuleFor(c => c.File).NotNull().OverridePropertyName(FileErrors.Field);
}

internal sealed class GetEmployeeDocumentsQueryHandler(
    IHrDbContext db,
    IAccessScopeProvider scopes,
    IPositionHolders holders,
    ICurrentUser currentUser,
    IOrganizationLookup organization,
    IFileStore fileStore,
    TimeProvider timeProvider) : IQueryHandler<GetEmployeeDocumentsQuery, EmployeeDocumentList>
{
    public async Task<Result<EmployeeDocumentList>> Handle(GetEmployeeDocumentsQuery request, CancellationToken cancellationToken)
    {
        var days = await CompanyDays.ResolveAsync(db, organization, timeProvider, cancellationToken);
        if (!await EmployeeDocumentRules.IsVisibleAsync(db, scopes, holders, currentUser, days, request.EmployeeId, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        var employeeId = EmployeeId.From(request.EmployeeId);
        var documents = await db.EmployeeDocuments.AsNoTracking()
            .Where(d => d.EmployeeId == employeeId)
            .Select(d => new { Document = d, Version = EF.Property<uint>(d, ConcurrencyVersion.PropertyName) })
            .ToListAsync(cancellationToken);
        var typeIds = documents.Select(d => d.Document.TypeId).Distinct().ToList();
        var types = (await db.DocumentTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToListAsync(cancellationToken)).ToDictionary(t => t.Id);
        var documentIds = documents.Select(d => d.Document.Id.Value).ToList();
        var files = (await EmployeeDocumentRules.Files(db, documentIds).AsNoTracking()
                .Select(f => new { f.OwnerId, f.FileName, f.ContentType, f.Length, f.CreatedAt })
                .ToListAsync(cancellationToken))
            .GroupBy(f => f.OwnerId)
            .ToDictionary(g => g.Key, g => g.MaxBy(f => f.CreatedAt)!);
        var primary = await db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .SelectMany(e => e.Employments)
            .OrderByDescending(m => m.IsPrimary)
            .ThenByDescending(m => m.HireDate)
            .Select(m => m.CompanyId)
            .FirstAsync(cancellationToken);
        var today = days.Of(primary, timeProvider);
        var canManage = await EmployeeDocumentRules.IsManagedAsync(db, scopes, request.EmployeeId, cancellationToken);

        var items = documents
            .Select(d =>
            {
                var type = types.GetValueOrDefault(d.Document.TypeId);
                var file = files.GetValueOrDefault(d.Document.Id.Value);
                return new EmployeeDocumentItem(
                    d.Document.Id.Value,
                    d.Document.TypeId.Value,
                    type?.Code ?? string.Empty,
                    type?.Name.Get(currentUser.Culture, "tr-TR") ?? string.Empty,
                    d.Document.Number,
                    d.Document.IssuedOn,
                    d.Document.ValidUntil,
                    d.Document.ValidUntil is { } until ? until.DayNumber - today.DayNumber : null,
                    d.Document.Note,
                    file?.FileName,
                    file?.ContentType,
                    file?.Length,
                    d.Version,
                    d.Document.CreatedAt);
            })
            .OrderBy(d => d.TypeName, StringComparer.Ordinal)
            .ThenByDescending(d => d.ValidUntil)
            .ToList();
        var rules = fileStore.AttachmentRules;
        return new EmployeeDocumentList(items, canManage, [.. rules.Extensions.Order(StringComparer.Ordinal)], rules.MaxBytes);
    }
}

internal sealed class AddEmployeeDocumentCommandHandler(IHrDbContext db, IAccessScopeProvider scopes, IFileStore files)
    : ICommandHandler<AddEmployeeDocumentCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddEmployeeDocumentCommand request, CancellationToken cancellationToken)
    {
        if (!await EmployeeDocumentRules.IsManagedAsync(db, scopes, request.EmployeeId, cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        var details = await EmployeeDocumentRules.ResolveAsync(db, request, request.IssuedOn, request.ValidUntil, null, cancellationToken);
        if (details.IsFailure)
        {
            return details.Error;
        }

        var document = EmployeeDocument.Create(EmployeeId.From(request.EmployeeId), details.Value);
        if (document.IsFailure)
        {
            return document.Error;
        }

        var stored = await files.SaveAsync(
            HrDocumentFiles.OwnerType,
            document.Value.Id.Value,
            StoredFilePurposes.Attachment,
            request.File!,
            files.AttachmentRules,
            cancellationToken);
        if (stored.IsFailure)
        {
            return stored.Error;
        }

        db.EmployeeDocuments.Add(document.Value);
        db.StoredFiles.Add(stored.Value);
        return document.Value.Id.Value;
    }
}

internal sealed class UpdateEmployeeDocumentCommandHandler(IHrDbContext db, IAccessScopeProvider scopes) : ICommandHandler<UpdateEmployeeDocumentCommand>
{
    public async Task<Result> Handle(UpdateEmployeeDocumentCommand request, CancellationToken cancellationToken)
    {
        var document = await EmployeeDocumentRules.IsManagedAsync(db, scopes, request.EmployeeId, cancellationToken)
            ? await EmployeeDocumentRules.FindAsync(db, request.EmployeeId, request.DocumentId, cancellationToken)
            : null;
        if (document is null)
        {
            return CommonErrors.NotFound;
        }

        db.Entry(document).ExpectVersion(request.Version);
        var details = await EmployeeDocumentRules.ResolveAsync(db, request, request.IssuedOn, request.ValidUntil, document.TypeId, cancellationToken);
        return details.IsFailure ? details.Error : document.Update(details.Value);
    }
}

internal sealed class ReplaceEmployeeDocumentFileCommandHandler(IHrDbContext db, IAccessScopeProvider scopes, IFileStore files)
    : ICommandHandler<ReplaceEmployeeDocumentFileCommand>
{
    public async Task<Result> Handle(ReplaceEmployeeDocumentFileCommand request, CancellationToken cancellationToken)
    {
        var document = await EmployeeDocumentRules.IsManagedAsync(db, scopes, request.EmployeeId, cancellationToken)
            ? await EmployeeDocumentRules.FindAsync(db, request.EmployeeId, request.DocumentId, cancellationToken)
            : null;
        if (document is null)
        {
            return CommonErrors.NotFound;
        }

        var stored = await files.SaveAsync(
            HrDocumentFiles.OwnerType,
            document.Id.Value,
            StoredFilePurposes.Attachment,
            request.File!,
            files.AttachmentRules,
            cancellationToken);
        if (stored.IsFailure)
        {
            return stored.Error;
        }

        await EmployeeDocumentRules.RemoveFilesAsync(db, files, document.Id.Value, cancellationToken);
        db.StoredFiles.Add(stored.Value);
        return Result.Success();
    }
}

internal sealed class DeleteEmployeeDocumentCommandHandler(IHrDbContext db, IAccessScopeProvider scopes, IFileStore files) : ICommandHandler<DeleteEmployeeDocumentCommand>
{
    public async Task<Result> Handle(DeleteEmployeeDocumentCommand request, CancellationToken cancellationToken)
    {
        var document = await EmployeeDocumentRules.IsManagedAsync(db, scopes, request.EmployeeId, cancellationToken)
            ? await EmployeeDocumentRules.FindAsync(db, request.EmployeeId, request.DocumentId, cancellationToken)
            : null;
        if (document is null)
        {
            return CommonErrors.NotFound;
        }

        await EmployeeDocumentRules.RemoveFilesAsync(db, files, document.Id.Value, cancellationToken);
        db.EmployeeDocuments.Remove(document);
        return Result.Success();
    }
}

internal sealed class GetEmployeeDocumentFileQueryHandler(
    IHrDbContext db,
    IAccessScopeProvider scopes,
    IPositionHolders holders,
    ICurrentUser currentUser,
    IOrganizationLookup organization,
    IFileStore files,
    TimeProvider timeProvider) : IQueryHandler<GetEmployeeDocumentFileQuery, HrFileDownload>
{
    public async Task<Result<HrFileDownload>> Handle(GetEmployeeDocumentFileQuery request, CancellationToken cancellationToken)
    {
        var days = await CompanyDays.ResolveAsync(db, organization, timeProvider, cancellationToken);
        if (!await EmployeeDocumentRules.IsVisibleAsync(db, scopes, holders, currentUser, days, request.EmployeeId, cancellationToken)
            || !await db.EmployeeDocuments.AnyAsync(
                d => d.Id == EmployeeDocumentId.From(request.DocumentId) && d.EmployeeId == EmployeeId.From(request.EmployeeId),
                cancellationToken))
        {
            return CommonErrors.NotFound;
        }

        var file = await EmployeeDocumentRules.Files(db, [request.DocumentId]).AsNoTracking()
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
