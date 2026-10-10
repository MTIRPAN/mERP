using Merp.Infrastructure.Files;
using Merp.Infrastructure.MultiTenancy;
using Merp.Modules.Platform.IntegrationTests.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.Modules.Platform.IntegrationTests.Files;

/// <summary>The nightly sweep of file content (D31).</summary>
public sealed class StoredFileSweepTests(OrganizationFactory factory) : IClassFixture<OrganizationFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sweep_RemovesOldOrphans_AndContentOfDeletedFiles_ButKeepsLiveFiles()
    {
        var admin = factory.Admin();
        var companyId = await OrganizationFactory.CreateCompanyAsync(admin);
        var live = await AttachmentEndpointsTests.UploadAsync(admin, companyId);
        var deleted = await AttachmentEndpointsTests.UploadAsync(admin, companyId);
        (await admin.DeleteAsync($"/api/platform/attachments/{deleted}", Ct)).EnsureSuccessStatusCode();

        var tenant = (await factory.Services.GetRequiredService<ITenantStore>().FindByCodeAsync("acme", Ct))!;
        var liveKey = await KeyOfAsync(live);
        var deletedKey = await KeyOfAsync(deleted);
        // As if removing the content after the delete had failed
        await WriteAsync(deletedKey, DateTime.UtcNow);
        // Left by a rolled back command two days ago, and by one a minute ago (still within the grace period)
        var oldOrphan = $"{tenant.Id:N}/platform/2026/01/{Guid.NewGuid():N}";
        var newOrphan = $"{tenant.Id:N}/platform/2026/01/{Guid.NewGuid():N}";
        await WriteAsync(oldOrphan, DateTime.UtcNow.AddDays(-2));
        await WriteAsync(newOrphan, DateTime.UtcNow.AddMinutes(-1));

        await ActivatorUtilities.CreateInstance<StoredFileSweepJob>(factory.Services).RunAsync(Ct);

        File.Exists(PathOf(liveKey)).ShouldBeTrue();
        File.Exists(PathOf(deletedKey)).ShouldBeFalse();
        File.Exists(PathOf(oldOrphan)).ShouldBeFalse();
        File.Exists(PathOf(newOrphan)).ShouldBeTrue();
        var stamped = await factory.QueryAsync("acme", db => db.StoredFiles.IgnoreQueryFilters().AsNoTracking().SingleAsync(f => f.Id == deleted, Ct));
        stamped.PurgedAt.ShouldNotBeNull();
    }

    private Task<string> KeyOfAsync(Guid id) =>
        factory.QueryAsync("acme", db => db.StoredFiles.IgnoreQueryFilters().AsNoTracking().Where(f => f.Id == id).Select(f => f.StorageKey).SingleAsync(Ct));

    private string PathOf(string key) => Path.Combine(factory.FilesRoot, key);

    private async Task WriteAsync(string key, DateTime lastWriteUtc)
    {
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, [1, 2, 3], Ct);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
    }
}
