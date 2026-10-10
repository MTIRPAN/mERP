using System.Globalization;
using System.Security.Cryptography;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Files;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Persistence.Files;
using Merp.Infrastructure.Persistence;
using Merp.SharedKernel.Results;
using Microsoft.Extensions.Options;

namespace Merp.Infrastructure.Files;

/// <inheritdoc cref="IFileStore"/>
internal sealed class FileStore(
    IFileStorage storage,
    IEnumerable<ModuleDatabase> modules,
    ITenantContext tenantContext,
    IAfterCommit afterCommit,
    TimeProvider timeProvider,
    IOptions<FileStorageOptions> options) : IFileStore
{
    public FileRules AttachmentRules { get; } = new(
        options.Value.AllowedExtensions.Select(e => e.Trim().TrimStart('.').ToLowerInvariant()).ToHashSet(StringComparer.Ordinal),
        options.Value.MaxBytes);

    public async Task<Result<StoredFile>> SaveAsync(
        string ownerType,
        Guid ownerId,
        string purpose,
        FileUpload upload,
        FileRules rules,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(rules);
        var schema = ModuleOf(ownerType);
        var content = upload.Content;
        if (!content.CanSeek)
        {
            throw new ArgumentException("The upload must be seekable: it is inspected and hashed before it is stored.", nameof(upload));
        }

        if (content.Length == 0)
        {
            return FileErrors.Empty;
        }

        if (content.Length > rules.MaxBytes)
        {
            return FileErrors.TooLarge(rules.MaxBytes);
        }

        var fileName = CleanFileName(upload.FileName);
        var extension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        if (extension.Length == 0 || extension.Length > StoredFile.ExtensionMaxLength || !rules.Extensions.Contains(extension))
        {
            return FileErrors.ExtensionNotAllowed(rules.Extensions);
        }

        var contentType = FileContentInspector.Inspect(extension, content);
        if (contentType is null)
        {
            return FileErrors.ContentMismatch;
        }

        content.Position = 0;
        var sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(content, cancellationToken));

        var id = Guid.CreateVersion7();
        var now = timeProvider.GetUtcNow();
        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"{tenantContext.RequiredTenant.Id:N}/{schema}/{now:yyyy}/{now:MM}/{id:N}");

        content.Position = 0;
        await storage.WriteAsync(key, content, contentType, cancellationToken);

        return StoredFile.Create(id, ownerType, ownerId, purpose, fileName, extension, contentType, content.Length, sha256, key);
    }

    public Task<Stream> OpenReadAsync(StoredFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        return storage.OpenReadAsync(file.StorageKey, cancellationToken);
    }

    public void PurgeAfterCommit(StoredFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var key = file.StorageKey;
        // The sweep stamps the row and retries if this fails
        afterCommit.Enqueue(cancellationToken => storage.DeleteAsync(key, cancellationToken));
    }

    /// <summary>The module of <c>{module}.{type}</c>; its schema keeps the metadata.</summary>
    private string ModuleOf(string ownerType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerType);
        var schema = ownerType.Split('.')[0];
        return modules.Any(m => string.Equals(m.Schema, schema, StringComparison.Ordinal))
            ? schema
            : throw new InvalidOperationException($"No module owns the file owner type {ownerType}.");
    }

    /// <summary>The name without the client's folders and control characters, at most 255 characters with its extension.</summary>
    internal static string CleanFileName(string? fileName)
    {
        var name = (fileName ?? string.Empty).Split('/', '\\')[^1];
        name = new string([.. name.Where(c => !char.IsControl(c))]).Trim().TrimStart('.');
        if (name.Length == 0)
        {
            return "file";
        }

        if (name.Length <= StoredFile.FileNameMaxLength)
        {
            return name;
        }

        var extension = Path.GetExtension(name);
        return extension.Length < 20
            ? name[..(StoredFile.FileNameMaxLength - extension.Length)] + extension
            : name[..StoredFile.FileNameMaxLength];
    }
}
