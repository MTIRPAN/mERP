using System.Runtime.CompilerServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Merp.Infrastructure.Files;

/// <summary>Files in a folder on the server; each key is a relative path under the root.</summary>
internal sealed class FileSystemFileStorage : IFileStorage
{
    private const string TemporarySuffix = ".partial";
    private readonly string _root;

    public FileSystemFileStorage(IOptions<FileStorageOptions> options, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        _root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.FileSystem.RootPath));
    }

    internal FileSystemFileStorage(string root) => _root = Path.GetFullPath(root);

    public async Task WriteAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Written under a temporary name and moved, so a reader never sees half a file
        var temporary = path + TemporarySuffix;
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await content.CopyToAsync(file, cancellationToken);
            }

            File.Move(temporary, path, overwrite: false);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathOf(key);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The stored file '{key}' is missing.", key);
        }

        return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        File.Delete(PathOf(key));
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<StorageObject> ListAsync(string prefix, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        StorageKeys.Validate(prefix.TrimEnd('/'));
        var folder = PathOf(prefix.TrimEnd('/'));
        if (!Directory.Exists(folder))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = Path.GetRelativePath(_root, path).Replace(Path.DirectorySeparatorChar, '/');
            // Leftovers of interrupted writes are listed too, so the sweep removes them
            var lastModified = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            yield return new StorageObject(key, lastModified);
        }

        await Task.CompletedTask;
    }

    private string PathOf(string key)
    {
        var name = key.EndsWith(TemporarySuffix, StringComparison.Ordinal) ? key[..^TemporarySuffix.Length] : key;
        StorageKeys.Validate(name);
        var path = Path.GetFullPath(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar)));
        return path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? path
            : throw new ArgumentException($"'{key}' is outside the storage folder.", nameof(key));
    }
}
