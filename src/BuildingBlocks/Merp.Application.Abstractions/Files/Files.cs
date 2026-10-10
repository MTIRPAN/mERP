using Merp.SharedKernel.Results;

namespace Merp.Application.Abstractions.Files;

/// <summary>A file sent by the client. <see cref="Content"/> must be seekable: it is inspected, hashed and then stored.</summary>
/// <param name="FileName">Name on the client's disk; only the name and extension are kept.</param>
/// <param name="Content">The bytes, read from the start.</param>
public sealed record FileUpload(string FileName, Stream Content);

/// <summary>Which files a use accepts (PLAN P7): extensions without the dot, lower case, and the size limit.</summary>
public sealed record FileRules(IReadOnlySet<string> Extensions, long MaxBytes)
{
    /// <summary>Raster images a browser and the PDF renderer show: PNG, JPEG and WebP (SVG can carry scripts).</summary>
    public static FileRules Images(long maxBytes) =>
        new(new HashSet<string>(["png", "jpg", "jpeg", "webp"], StringComparer.Ordinal), maxBytes);
}

/// <summary>What the current user may do with the files of one record.</summary>
public enum FileOwnerAccess
{
    /// <summary>The record is missing or outside the user's scope: the files do not exist for the user (404).</summary>
    None,

    /// <summary>List and download.</summary>
    Read,

    /// <summary>Also upload and delete.</summary>
    Manage,
}

/// <summary>
/// Decides who sees the files of a record type, e.g. <c>platform.company</c> (D31). The module that owns the record
/// implements it with the record's own permissions and data scope, so files never need permissions of their own.
/// </summary>
public interface IFileAccessPolicy
{
    /// <summary>Record type, <c>{module}.{type}</c>; the module keeps the files in its schema.</summary>
    string OwnerType { get; }

    Task<FileOwnerAccess> GetAccessAsync(Guid ownerId, CancellationToken cancellationToken = default);
}

public static class FileErrors
{
    public const string Field = "file";

    public static readonly Error Empty = Error.Validation("common.files.errors.empty").ForField(Field);

    public static Error TooLarge(long maxBytes) =>
        Error.Validation("common.files.errors.too_large", new Dictionary<string, object?> { ["maxMegabytes"] = ToMegabytes(maxBytes) }).ForField(Field);

    public static Error ExtensionNotAllowed(IEnumerable<string> allowed) =>
        Error.Validation(
            "common.files.errors.extension_not_allowed",
            new Dictionary<string, object?> { ["extensions"] = string.Join(", ", allowed.Order(StringComparer.Ordinal)) })
        .ForField(Field);

    /// <summary>The bytes are not what the extension says, e.g. a program renamed to <c>.pdf</c>.</summary>
    public static readonly Error ContentMismatch = Error.Validation("common.files.errors.content_mismatch").ForField(Field);

    private static decimal ToMegabytes(long bytes) => decimal.Round(bytes / (1024m * 1024m), 1);
}

/// <summary>The access policies of all modules by owner type; an unknown owner type gives no access.</summary>
public sealed class FileAccessPolicies(IEnumerable<IFileAccessPolicy> policies)
{
    private readonly Dictionary<string, IFileAccessPolicy> _policies = policies.ToDictionary(p => p.OwnerType, StringComparer.Ordinal);

    public bool IsKnown(string? ownerType) => ownerType is not null && _policies.ContainsKey(ownerType);

    public Task<FileOwnerAccess> GetAccessAsync(string? ownerType, Guid ownerId, CancellationToken cancellationToken = default) =>
        ownerType is not null && _policies.TryGetValue(ownerType, out var policy)
            ? policy.GetAccessAsync(ownerId, cancellationToken)
            : Task.FromResult(FileOwnerAccess.None);
}
