using FluentValidation;
using Merp.Application.Abstractions.Files;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Persistence.Files;
using Merp.Modules.Platform.Domain.Users;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.Platform.Application.Files;

/// <summary>
/// Attachments of a Platform record, newest first, whether the caller may add or delete, and which files are accepted
/// (PLAN P7).
/// </summary>
public sealed record GetAttachmentsQuery(string? OwnerType, Guid OwnerId) : IQuery<AttachmentList>;

public sealed record AttachmentList(IReadOnlyList<AttachmentItem> Items, bool CanManage, IReadOnlyList<string> Extensions, long MaxBytes);

public sealed record AttachmentItem(Guid Id, string FileName, string ContentType, long Length, DateTimeOffset CreatedAt, string? CreatedBy);

/// <summary>Attaches a file to a Platform record; needs the record's manage permission (<see cref="IFileAccessPolicy"/>).</summary>
public sealed record UploadAttachmentCommand(string? OwnerType, Guid OwnerId, FileUpload? File) : ICommand<Guid>;

/// <summary>Deletes an attachment; its content is removed from storage after the commit.</summary>
public sealed record DeleteAttachmentCommand(Guid Id) : ICommand;

/// <summary>Content of a Platform file (an attachment or a logo) for download; 404 unless the caller sees its record.</summary>
public sealed record GetFileQuery(Guid Id) : IQuery<FileDownload>;

/// <summary>The caller disposes <see cref="Content"/>.</summary>
public sealed record FileDownload(string FileName, string ContentType, long Length, Stream Content);

internal sealed class GetAttachmentsQueryValidator : AbstractValidator<GetAttachmentsQuery>
{
    public GetAttachmentsQueryValidator() => RuleFor(q => q.OwnerType).NotEmpty();
}

internal sealed class UploadAttachmentCommandValidator : AbstractValidator<UploadAttachmentCommand>
{
    public UploadAttachmentCommandValidator()
    {
        RuleFor(c => c.OwnerType).NotEmpty();
        RuleFor(c => c.File).NotNull().OverridePropertyName(FileErrors.Field);
    }
}

internal sealed class GetAttachmentsQueryHandler(IPlatformDbContext db, FileAccessPolicies policies, IFileStore fileStore) : IQueryHandler<GetAttachmentsQuery, AttachmentList>
{
    public async Task<Result<AttachmentList>> Handle(GetAttachmentsQuery request, CancellationToken cancellationToken)
    {
        var access = await PlatformFileAccess.GetAsync(policies, request.OwnerType, request.OwnerId, cancellationToken);
        if (access == FileOwnerAccess.None)
        {
            return CommonErrors.NotFound;
        }

        var files = await db.StoredFiles
            .AsNoTracking()
            .Where(f => f.OwnerType == request.OwnerType && f.OwnerId == request.OwnerId && f.Purpose == StoredFilePurposes.Attachment)
            .OrderByDescending(f => f.CreatedAt)
            .ThenByDescending(f => f.Id)
            .Select(f => new { f.Id, f.FileName, f.ContentType, f.Length, f.CreatedAt, f.CreatedBy })
            .ToListAsync(cancellationToken);

        var userIds = files.Where(f => f.CreatedBy is not null).Select(f => UserId.From(f.CreatedBy!.Value)).Distinct().ToList();
        var users = await db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.UserName })
            .ToListAsync(cancellationToken);
        var names = users.ToDictionary(u => u.Id.Value, u => FullName(u.FirstName, u.LastName) ?? u.UserName);

        var items = files
            .Select(f => new AttachmentItem(
                f.Id,
                f.FileName,
                f.ContentType,
                f.Length,
                f.CreatedAt,
                f.CreatedBy is { } userId ? names.GetValueOrDefault(userId) : null))
            .ToList();
        var rules = fileStore.AttachmentRules;
        return new AttachmentList(items, access == FileOwnerAccess.Manage, [.. rules.Extensions.Order(StringComparer.Ordinal)], rules.MaxBytes);
    }

    private static string? FullName(string? firstName, string? lastName)
    {
        var name = $"{firstName} {lastName}".Trim();
        return name.Length > 0 ? name : null;
    }
}

internal sealed class UploadAttachmentCommandHandler(IPlatformDbContext db, FileAccessPolicies policies, IFileStore files)
    : ICommandHandler<UploadAttachmentCommand, Guid>
{
    public async Task<Result<Guid>> Handle(UploadAttachmentCommand request, CancellationToken cancellationToken)
    {
        var access = PlatformFileAccess.RequireManage(await PlatformFileAccess.GetAsync(policies, request.OwnerType, request.OwnerId, cancellationToken));
        if (access.IsFailure)
        {
            return access.Error;
        }

        var stored = await files.SaveAsync(request.OwnerType!, request.OwnerId, StoredFilePurposes.Attachment, request.File!, files.AttachmentRules, cancellationToken);
        if (stored.IsFailure)
        {
            return stored.Error;
        }

        db.StoredFiles.Add(stored.Value);
        return stored.Value.Id;
    }
}

internal sealed class DeleteAttachmentCommandHandler(IPlatformDbContext db, FileAccessPolicies policies, IFileStore files)
    : ICommandHandler<DeleteAttachmentCommand>
{
    public async Task<Result> Handle(DeleteAttachmentCommand request, CancellationToken cancellationToken)
    {
        var file = await db.StoredFiles.SingleOrDefaultAsync(
            f => f.Id == request.Id && f.Purpose == StoredFilePurposes.Attachment,
            cancellationToken);
        if (file is null)
        {
            return CommonErrors.NotFound;
        }

        var access = PlatformFileAccess.RequireManage(await PlatformFileAccess.GetAsync(policies, file.OwnerType, file.OwnerId, cancellationToken));
        if (access.IsFailure)
        {
            return access.Error;
        }

        db.StoredFiles.Remove(file);
        files.PurgeAfterCommit(file);
        return Result.Success();
    }
}

internal sealed class GetFileQueryHandler(IPlatformDbContext db, FileAccessPolicies policies, IFileStore files) : IQueryHandler<GetFileQuery, FileDownload>
{
    public async Task<Result<FileDownload>> Handle(GetFileQuery request, CancellationToken cancellationToken)
    {
        var file = await db.StoredFiles.AsNoTracking().SingleOrDefaultAsync(f => f.Id == request.Id, cancellationToken);
        if (file is null || await PlatformFileAccess.GetAsync(policies, file.OwnerType, file.OwnerId, cancellationToken) == FileOwnerAccess.None)
        {
            return CommonErrors.NotFound;
        }

        var content = await files.OpenReadAsync(file, cancellationToken);
        return new FileDownload(file.FileName, file.ContentType, file.Length, content);
    }
}
