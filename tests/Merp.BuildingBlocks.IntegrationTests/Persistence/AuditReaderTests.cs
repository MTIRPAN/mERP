using Merp.Application.Abstractions.Auditing;
using Merp.Infrastructure.Persistence.Auditing;
using Merp.Testing.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.BuildingBlocks.IntegrationTests.Persistence;

/// <summary>Reading a module's audit trail and logging reads of sensitive fields (PLAN P3, D13).</summary>
public sealed class AuditReaderTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WidgetHarness _harness = null!;

    public async ValueTask InitializeAsync() => _harness = await WidgetHarness.CreateAsync(postgres);

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task EveryModule_RegistersAReader_ThatListsItsAuditedTypes()
    {
        await using var scope = _harness.CreateScope();

        var reader = scope.ServiceProvider.GetServices<IAuditReader>().ShouldHaveSingleItem();

        reader.Module.ShouldBe(WidgetDbContext.SchemaName);
        reader.EntityTypes.ShouldBe([nameof(Widget)]);
    }

    [Fact]
    public async Task Reader_FiltersByRecordAndPeriod_NewestFirst()
    {
        var id = (await _harness.SendAsync(new CreateWidget("Masa"))).Value;
        var other = (await _harness.SendAsync(new CreateWidget("Dolap"))).Value;
        _harness.Time.Advance(TimeSpan.FromHours(1));
        var renamedAt = _harness.Time.GetUtcNow();
        await _harness.SendAsync(new RenameWidget(id, "Sandalye"));

        await using var scope = _harness.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditReader>();
        var history = new AuditFilter(nameof(Widget), id.Value.ToString());

        var records = await reader.ReadAsync(history, take: 100, Ct);
        records.ShouldNotContain(r => r.EntityId == other.Value.ToString());
        records[0].ShouldBe(records.Single(r => r.Action == "Update"));
        (records[0] with { Id = Guid.Empty, CorrelationId = null }).ShouldBe(new AuditRecord(
            Guid.Empty, "widgets", renamedAt, WidgetHarness.UserId, nameof(Widget), id.Value.ToString(), "Update", nameof(Widget.Name), "Masa", "Sandalye", null));
        (await reader.CountAsync(history, Ct)).ShouldBe(records.Count);

        (await reader.ReadAsync(history with { From = renamedAt }, take: 100, Ct)).ShouldHaveSingleItem().Action.ShouldBe("Update");
        (await reader.ReadAsync(history with { To = renamedAt.AddTicks(-1) }, take: 100, Ct)).ShouldAllBe(r => r.Action == "Insert");
        (await reader.ReadAsync(new AuditFilter(UserId: Guid.NewGuid()), take: 100, Ct)).ShouldBeEmpty();
        (await reader.ReadAsync(new AuditFilter(), take: 2, Ct)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task SensitiveRead_IsLoggedPerField_WithoutSavingTheRequestsPendingChanges()
    {
        var id = (await _harness.SendAsync(new CreateWidget("Masa", Secret: "gizli"))).Value;

        await using (var scope = _harness.CreateScope())
        {
            // A change the request has not committed must not be saved along with the read entries
            var db = scope.ServiceProvider.GetRequiredService<WidgetDbContext>();
            (await db.Widgets.SingleAsync(w => w.Id == id, Ct)).Rename("Kaydedilmedi");

            await scope.ServiceProvider.GetRequiredService<ISensitiveReadLog>().RecordAsync<Widget>(id.Value.ToString(), Ct);
        }

        var reads = await _harness.QueryAsync(db => db.AuditEntries.Where(a => a.Action == AuditAction.Read).ToListAsync(Ct));
        var read = reads.ShouldHaveSingleItem();
        (read.EntityType, read.EntityId, read.Field, read.UserId).ShouldBe((nameof(Widget), id.Value.ToString(), nameof(Widget.Secret), WidgetHarness.UserId));
        read.OldValue.ShouldBeNull();
        read.NewValue.ShouldBeNull();
        (await _harness.QueryAsync(db => db.Widgets.SingleAsync(w => w.Id == id, Ct))).Name.ShouldBe("Masa");
    }

    [Fact]
    public async Task SensitiveRead_OfATypeNoModuleMaps_Throws()
    {
        await using var scope = _harness.CreateScope();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<ISensitiveReadLog>().RecordAsync(typeof(string), "x", ["Length"], Ct));
    }
}
