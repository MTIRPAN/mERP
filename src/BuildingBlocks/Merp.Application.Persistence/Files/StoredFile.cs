using Merp.Application.Abstractions.Files;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;

namespace Merp.Application.Persistence.Files;

/// <summary>
/// Metadata of a file in storage (D16, D31). Every module keeps its files in its own <c>stored_files</c> table, next
/// to the records they belong to; the bytes are in <c>IFileStorage</c> under <see cref="StorageKey"/>. Deleting is a
/// soft delete; the bytes are removed after the commit and the row stays for the audit trail.
/// </summary>
[Audited]
public sealed class StoredFile : IAuditable, ISoftDeletable
{
    public const int OwnerTypeMaxLength = 100;
    public const int PurposeMaxLength = 30;
    public const int FileNameMaxLength = 255;
    public const int ExtensionMaxLength = 10;
    public const int ContentTypeMaxLength = 100;
    public const int StorageKeyMaxLength = 300;

    private StoredFile()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Record type the file belongs to, <c>{module}.{type}</c> (<see cref="IFileAccessPolicy.OwnerType"/>).</summary>
    public string OwnerType { get; private set; } = null!;

    public Guid OwnerId { get; private set; }

    /// <summary>Role of the file for its record, see <see cref="StoredFilePurposes"/>.</summary>
    public string Purpose { get; private set; } = null!;

    /// <summary>Name the user uploaded, without the client's folders.</summary>
    public string FileName { get; private set; } = null!;

    /// <summary>Lower case extension without the dot; the content was checked against it.</summary>
    public string Extension { get; private set; } = null!;

    /// <summary>Media type derived from the checked content, never the one the client sent.</summary>
    public string ContentType { get; private set; } = null!;

    public long Length { get; private set; }

    /// <summary>SHA-256 of the content, lower case hex.</summary>
    public string Sha256 { get; private set; } = null!;

    public string StorageKey { get; private set; } = null!;

    /// <summary>When the bytes of a deleted file were removed from storage.</summary>
    public DateTimeOffset? PurgedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? ModifiedAt { get; private set; }

    public Guid? ModifiedBy { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    /// <summary>Called by the file store once the content is in storage.</summary>
    public static StoredFile Create(
        Guid id,
        string ownerType,
        Guid ownerId,
        string purpose,
        string fileName,
        string extension,
        string contentType,
        long length,
        string sha256,
        string storageKey) =>
        new()
        {
            Id = id,
            OwnerType = ownerType,
            OwnerId = ownerId,
            Purpose = purpose,
            FileName = fileName,
            Extension = extension,
            ContentType = contentType,
            Length = length,
            Sha256 = sha256,
            StorageKey = storageKey,
        };

    public void MarkPurged(DateTimeOffset at) => PurgedAt = at;
}

public static class StoredFilePurposes
{
    /// <summary>A document attached to a record by its users (<c>&lt;Attachments /&gt;</c>).</summary>
    public const string Attachment = "attachment";

    /// <summary>A record's single picture, e.g. a company logo.</summary>
    public const string Logo = "logo";

    /// <summary>A file uploaded to import records (D35).</summary>
    public const string Import = "import";
}

/// <summary>
/// Stores uploaded files for the command in progress (PLAN P7, D31). The command adds the returned
/// <see cref="StoredFile"/> to its module's DbContext, so the metadata commits or rolls back with the business data;
/// content left behind by a rolled back command is removed by the nightly sweep.
/// </summary>
public interface IFileStore
{
    /// <summary>Default rules for attachments (<c>Files:AllowedExtensions</c>, <c>Files:MaxBytes</c>).</summary>
    FileRules AttachmentRules { get; }

    /// <summary>
    /// Checks size, extension and content (magic bytes) against <paramref name="rules"/>, hashes the content and
    /// writes it to storage. Errors are validation errors on the <c>file</c> field.
    /// </summary>
    /// <param name="ownerType">Record type, <c>{module}.{type}</c>; its module's schema keeps the metadata.</param>
    /// <param name="ownerId">The record.</param>
    /// <param name="purpose">See <see cref="StoredFilePurposes"/>.</param>
    /// <param name="upload">The file.</param>
    /// <param name="rules">Accepted files.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    Task<Result<StoredFile>> SaveAsync(
        string ownerType,
        Guid ownerId,
        string purpose,
        FileUpload upload,
        FileRules rules,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the content; the caller disposes the stream.</summary>
    Task<Stream> OpenReadAsync(StoredFile file, CancellationToken cancellationToken = default);

    /// <summary>Removes the content once the command commits; the command removes (soft deletes) the metadata itself.</summary>
    void PurgeAfterCommit(StoredFile file);
}
