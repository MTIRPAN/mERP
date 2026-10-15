using System.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Merp.Infrastructure.Templates;

/// <summary>
/// The partials of a render (<c>{% render 'letterhead' %}</c>, D36) from memory. Fluid asks for <c>name.liquid</c>;
/// anything not given is not found, so templates cannot reach the file system.
/// </summary>
internal sealed class PartialFileProvider(IReadOnlyDictionary<string, string> partials) : IFileProvider
{
    private const string Extension = ".liquid";

    public IFileInfo GetFileInfo(string subpath)
    {
        var name = subpath.TrimStart('/', '\\');
        if (name.EndsWith(Extension, StringComparison.Ordinal))
        {
            name = name[..^Extension.Length];
        }

        return partials.TryGetValue(name, out var source) ? new PartialFileInfo(name, source) : new NotFoundFileInfo(name);
    }

    public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;

    private sealed class PartialFileInfo(string name, string source) : IFileInfo
    {
        private readonly byte[] _content = Encoding.UTF8.GetBytes(source);

        public bool Exists => true;

        public long Length => _content.Length;

        public string? PhysicalPath => null;

        public string Name => name + Extension;

        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        public bool IsDirectory => false;

        public Stream CreateReadStream() => new MemoryStream(_content, writable: false);
    }
}
