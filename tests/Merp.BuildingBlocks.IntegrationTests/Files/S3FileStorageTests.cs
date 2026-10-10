using System.Text;
using Merp.Infrastructure.Files;
using Merp.Testing.Containers;
using Microsoft.Extensions.Options;

namespace Merp.BuildingBlocks.IntegrationTests.Files;

/// <summary>The S3 storage against SeaweedFS, an S3 compatible store (D16, D31).</summary>
public sealed class S3FileStorageTests(SeaweedFsFixture seaweed) : IClassFixture<SeaweedFsFixture>, IDisposable
{
    private readonly S3FileStorage _storage = new(Options.Create(new FileStorageOptions
    {
        Storage = FileStorageKind.S3,
        S3 = new S3StorageOptions
        {
            ServiceUrl = seaweed.ServiceUrl,
            Bucket = SeaweedFsFixture.Bucket,
            AccessKey = SeaweedFsFixture.AccessKey,
            SecretKey = SeaweedFsFixture.SecretKey,
        },
    }));

    private readonly string _prefix = Guid.NewGuid().ToString("N");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Write_ThenRead_ReturnsTheSameBytes()
    {
        var key = $"{_prefix}/platform/2026/10/a1";
        var content = Encoding.UTF8.GetBytes("Sözleşme içeriği");

        await _storage.WriteAsync(key, new MemoryStream(content), "text/plain; charset=utf-8", Ct);

        await using var read = await _storage.OpenReadAsync(key, Ct);
        using var copy = new MemoryStream();
        await read.CopyToAsync(copy, Ct);
        copy.ToArray().ShouldBe(content);
    }

    [Fact]
    public async Task Read_OfAMissingKey_ThrowsFileNotFound() =>
        await Should.ThrowAsync<FileNotFoundException>(() => _storage.OpenReadAsync($"{_prefix}/platform/2026/10/missing", Ct));

    [Fact]
    public async Task Delete_RemovesTheObject_AndAMissingKeyIsNotAnError()
    {
        var key = $"{_prefix}/platform/2026/10/a1";
        await _storage.WriteAsync(key, new MemoryStream([1, 2, 3]), "application/pdf", Ct);

        await _storage.DeleteAsync(key, Ct);
        await _storage.DeleteAsync(key, Ct);

        await Should.ThrowAsync<FileNotFoundException>(() => _storage.OpenReadAsync(key, Ct));
    }

    [Fact]
    public async Task List_ReturnsTheKeysUnderThePrefix_WithTheirAge()
    {
        await _storage.WriteAsync($"{_prefix}/platform/2026/10/a1", new MemoryStream([1]), "text/plain", Ct);
        await _storage.WriteAsync($"{_prefix}/platform/2026/11/a2", new MemoryStream([1]), "text/plain", Ct);
        await _storage.WriteAsync($"{_prefix}/recruitment/2026/10/b1", new MemoryStream([1]), "text/plain", Ct);

        var items = new List<StorageObject>();
        await foreach (var item in _storage.ListAsync($"{_prefix}/platform/", Ct))
        {
            items.Add(item);
        }

        items.Select(i => i.Key).ShouldBe([$"{_prefix}/platform/2026/10/a1", $"{_prefix}/platform/2026/11/a2"], ignoreOrder: true);
        items.ShouldAllBe(i => i.LastModified > DateTimeOffset.MinValue);
    }

    public void Dispose() => _storage.Dispose();
}
