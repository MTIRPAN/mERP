using Merp.Infrastructure.Files;

namespace Merp.BuildingBlocks.UnitTests.Files;

public sealed class FileSystemFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "merp-unit", Guid.NewGuid().ToString("N"));
    private readonly FileSystemFileStorage _storage;

    public FileSystemFileStorageTests() => _storage = new FileSystemFileStorage(_root);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Write_ThenRead_ReturnsTheSameBytes()
    {
        await _storage.WriteAsync("t1/platform/2026/10/abc", new MemoryStream([1, 2, 3]), "application/pdf", Ct);

        await using var read = await _storage.OpenReadAsync("t1/platform/2026/10/abc", Ct);
        using var copy = new MemoryStream();
        await read.CopyToAsync(copy, Ct);
        copy.ToArray().ShouldBe([1, 2, 3]);
        Directory.EnumerateFiles(_root, "*.partial", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task Read_OfAMissingKey_ThrowsFileNotFound() =>
        await Should.ThrowAsync<FileNotFoundException>(() => _storage.OpenReadAsync("t1/platform/2026/10/missing", Ct));

    [Fact]
    public async Task Delete_RemovesTheFile_AndAMissingKeyIsNotAnError()
    {
        await _storage.WriteAsync("t1/platform/2026/10/abc", new MemoryStream([1]), "text/plain", Ct);

        await _storage.DeleteAsync("t1/platform/2026/10/abc", Ct);
        await _storage.DeleteAsync("t1/platform/2026/10/abc", Ct);

        await Should.ThrowAsync<FileNotFoundException>(() => _storage.OpenReadAsync("t1/platform/2026/10/abc", Ct));
    }

    [Fact]
    public async Task List_ReturnsTheKeysUnderThePrefixOnly()
    {
        await _storage.WriteAsync("t1/platform/2026/10/a1", new MemoryStream([1]), "text/plain", Ct);
        await _storage.WriteAsync("t1/platform/2026/11/a2", new MemoryStream([1]), "text/plain", Ct);
        await _storage.WriteAsync("t1/recruitment/2026/10/b1", new MemoryStream([1]), "text/plain", Ct);
        await _storage.WriteAsync("t2/platform/2026/10/c1", new MemoryStream([1]), "text/plain", Ct);

        var keys = new List<string>();
        await foreach (var item in _storage.ListAsync("t1/platform/", Ct))
        {
            keys.Add(item.Key);
        }

        keys.ShouldBe(["t1/platform/2026/10/a1", "t1/platform/2026/11/a2"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("t1/../../outside")]
    [InlineData("/t1/abc")]
    [InlineData("t1\\abc")]
    [InlineData("T1/ABC")]
    [InlineData("t1//abc")]
    public async Task KeysThatCouldLeaveTheFolder_AreRefused(string key) =>
        await Should.ThrowAsync<ArgumentException>(() => _storage.WriteAsync(key, new MemoryStream([1]), "text/plain", Ct));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
