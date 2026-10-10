namespace Merp.Infrastructure.Files;

public enum FileStorageKind
{
    /// <summary>A folder on the server (default, the simplest on-prem setup).</summary>
    FileSystem,

    /// <summary>Any S3 compatible store (AWS S3, SeaweedFS, Garage...), D16.</summary>
    S3,
}

/// <summary>The <c>Files</c> section (D16, D31).</summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "Files";

    public FileStorageKind Storage { get; set; } = FileStorageKind.FileSystem;

    /// <summary>Largest attachment in bytes (default 20 MB).</summary>
    public long MaxBytes { get; set; } = 20 * 1024 * 1024;

    /// <summary>
    /// Attachment extensions; each must be one whose content the server can check (<see cref="FileContentInspector"/>).
    /// </summary>
    public string[] AllowedExtensions { get; set; } = ["pdf", "docx", "xlsx", "pptx", "png", "jpg", "jpeg", "webp", "txt", "csv"];

    /// <summary>
    /// Content in storage without metadata (left by rolled back commands) is removed by the nightly sweep once it is
    /// older than this.
    /// </summary>
    public TimeSpan OrphanGracePeriod { get; set; } = TimeSpan.FromDays(1);

    public FileSystemStorageOptions FileSystem { get; set; } = new();

    public S3StorageOptions S3 { get; set; } = new();
}

public sealed class FileSystemStorageOptions
{
    /// <summary>Folder of the files; relative paths are under the host's content root.</summary>
    public string RootPath { get; set; } = "App_Data/files";
}

public sealed class S3StorageOptions
{
    /// <summary>Endpoint of an S3 compatible store, e.g. <c>http://localhost:8333</c>; empty for AWS (uses <see cref="Region"/>).</summary>
    public string? ServiceUrl { get; set; }

    public string Region { get; set; } = "us-east-1";

    /// <summary>The bucket must exist; the application does not create it.</summary>
    public string Bucket { get; set; } = string.Empty;

    public string? AccessKey { get; set; }

    public string? SecretKey { get; set; }

    /// <summary>Path style addressing (<c>host/bucket/key</c>), needed by most self hosted stores.</summary>
    public bool ForcePathStyle { get; set; } = true;
}
