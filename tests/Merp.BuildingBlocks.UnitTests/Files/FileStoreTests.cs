using System.Security.Cryptography;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Files;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Persistence.Files;
using Merp.Infrastructure.Files;
using Merp.Infrastructure.Messaging;
using Merp.Infrastructure.Persistence;
using Merp.Testing.Data;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Merp.BuildingBlocks.UnitTests.Files;

public sealed class FileStoreTests
{
    private static readonly Guid TenantId = Guid.Parse("0199d000-0000-7000-8000-000000000001");
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();
    private readonly AfterCommitActions _afterCommit = new();
    private readonly FileStore _store;

    public FileStoreTests()
    {
        var modules = new[] { new ModuleDatabase(typeof(object), "platform", new ModuleScope(typeof(FileStoreTests).Assembly)) };
        var options = Options.Create(new FileStorageOptions { MaxBytes = 1024, AllowedExtensions = ["pdf", "PNG", ".txt"] });
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));
        _store = new FileStore(_storage, modules, new FixedTenant(), _afterCommit, time, options);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void AttachmentRules_AreNormalizedFromConfiguration()
    {
        _store.AttachmentRules.Extensions.ShouldBe(["pdf", "png", "txt"], ignoreOrder: true);
        _store.AttachmentRules.MaxBytes.ShouldBe(1024);
    }

    [Fact]
    public async Task Save_StoresTheContent_AndDescribesIt()
    {
        var content = SampleFiles.Pdf();

        var result = await _store.SaveAsync("platform.company", Guid.Empty, StoredFilePurposes.Attachment, Upload(@"C:\Users\ayşe\Belgeler\Sözleşme.PDF", content), _store.AttachmentRules, Ct);

        result.IsSuccess.ShouldBeTrue();
        var file = result.Value;
        file.FileName.ShouldBe("Sözleşme.PDF");
        file.Extension.ShouldBe("pdf");
        file.ContentType.ShouldBe("application/pdf");
        file.Length.ShouldBe(content.Length);
        file.Sha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(content)));
        file.OwnerType.ShouldBe("platform.company");
        file.Purpose.ShouldBe(StoredFilePurposes.Attachment);
        file.StorageKey.ShouldBe($"{TenantId:N}/platform/2026/10/{file.Id:N}");
        await _storage.Received(1).WriteAsync(file.StorageKey, Arg.Any<Stream>(), "application/pdf", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_OfAnEmptyFile_FailsOnTheFileField()
    {
        var result = await _store.SaveAsync("platform.company", Guid.Empty, StoredFilePurposes.Attachment, Upload("a.pdf", []), _store.AttachmentRules, Ct);

        var error = result.Error.Fields.ShouldHaveSingleItem();
        error.Field.ShouldBe("file");
        error.Code.ShouldBe("common.files.errors.empty");
        await _storage.DidNotReceive().WriteAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_OfATooLargeFile_NamesTheLimitInMegabytes()
    {
        var rules = new FileRules(new HashSet<string>(["pdf"]), 2 * 1024 * 1024);
        var content = SampleFiles.Pdf().Concat(new byte[(2 * 1024 * 1024) + 1]).ToArray();

        var result = await _store.SaveAsync("platform.company", Guid.Empty, StoredFilePurposes.Attachment, Upload("a.pdf", content), rules, Ct);

        var error = result.Error.Fields.ShouldHaveSingleItem();
        error.Code.ShouldBe("common.files.errors.too_large");
        error.Args["maxMegabytes"].ShouldBe(2m);
    }

    [Theory]
    [InlineData("program.exe")]
    [InlineData("noextension")]
    [InlineData("logo.svg")]
    public async Task Save_WithAnExtensionOutsideTheRules_ListsTheAllowedOnes(string fileName)
    {
        var result = await _store.SaveAsync("platform.company", Guid.Empty, StoredFilePurposes.Attachment, Upload(fileName, SampleFiles.Pdf()), _store.AttachmentRules, Ct);

        var error = result.Error.Fields.ShouldHaveSingleItem();
        error.Code.ShouldBe("common.files.errors.extension_not_allowed");
        error.Args["extensions"].ShouldBe("pdf, png, txt");
    }

    [Fact]
    public async Task Save_OfAProgramRenamedToPdf_IsRefused()
    {
        var result = await _store.SaveAsync("platform.company", Guid.Empty, StoredFilePurposes.Attachment, Upload("fatura.pdf", SampleFiles.Executable()), _store.AttachmentRules, Ct);

        result.Error.Fields.ShouldHaveSingleItem().Code.ShouldBe("common.files.errors.content_mismatch");
        await _storage.DidNotReceive().WriteAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_ForAnOwnerTypeOfNoModule_Throws() =>
        await Should.ThrowAsync<InvalidOperationException>(() =>
            _store.SaveAsync("hr.employee", Guid.Empty, StoredFilePurposes.Attachment, Upload("a.pdf", SampleFiles.Pdf()), _store.AttachmentRules, Ct));

    [Fact]
    public async Task Purge_DeletesTheContentOnlyAfterTheCommit()
    {
        var file = (await _store.SaveAsync("platform.company", Guid.Empty, StoredFilePurposes.Attachment, Upload("a.pdf", SampleFiles.Pdf()), _store.AttachmentRules, Ct)).Value;

        _store.PurgeAfterCommit(file);

        await _storage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _afterCommit.RunAsync(Ct);
        await _storage.Received(1).DeleteAsync(file.StorageKey, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("rapor.pdf", "rapor.pdf")]
    [InlineData("../../etc/passwd.txt", "passwd.txt")]
    [InlineData("C:\\temp\\a.png", "a.png")]
    [InlineData("  .gizli.pdf ", "gizli.pdf")]
    [InlineData("a\u0000b\u001F.pdf", "ab.pdf")]
    [InlineData("", "file")]
    public void CleanFileName_KeepsOnlyASafeName(string input, string expected) =>
        FileStore.CleanFileName(input).ShouldBe(expected);

    [Fact]
    public void CleanFileName_ShortensLongNames_AndKeepsTheExtension()
    {
        var name = FileStore.CleanFileName(new string('a', 400) + ".pdf");

        name.Length.ShouldBe(StoredFile.FileNameMaxLength);
        name.ShouldEndWith(".pdf");
    }

    private static FileUpload Upload(string name, byte[] content) => new(name, new MemoryStream(content));

    private sealed class FixedTenant : ITenantContext
    {
        public TenantInfo? Tenant => new(TenantId, "acme", "Acme", "tr-TR");
    }

    private sealed class AfterCommitActions : IAfterCommit
    {
        private readonly List<Func<CancellationToken, Task>> _actions = [];

        public void Enqueue(Func<CancellationToken, Task> action) => _actions.Add(action);

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            foreach (var action in _actions)
            {
                await action(cancellationToken);
            }
        }
    }
}
