using Merp.Infrastructure.Persistence;
using Merp.Infrastructure.Persistence.Auditing;
using Merp.Infrastructure.Persistence.Interceptors;
using Merp.SharedKernel.Results;
using Merp.Testing.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Merp.BuildingBlocks.IntegrationTests.Persistence;

public sealed class ModuleDbContextTests(PostgresFixture postgres) : IAsyncLifetime
{
    private WidgetHarness _harness = null!;

    public async ValueTask InitializeAsync() => _harness = await WidgetHarness.CreateAsync(postgres);

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task SavingAnEntity_SetsCreatedStamps_WritesAuditEntries_AndOutboxMessage()
    {
        var id = (await _harness.SendAsync(new CreateWidget("Masa", "Çalışma masası", Secret: "gizli"))).Value;

        var widget = await _harness.QueryAsync(db => db.Widgets.SingleAsync(w => w.Id == id));
        widget.CreatedBy.ShouldBe(WidgetHarness.UserId);
        widget.CreatedAt.ShouldBe(_harness.Time.GetUtcNow());
        widget.ModifiedAt.ShouldBeNull();
        widget.Title.Get("tr-TR", "tr-TR").ShouldBe("Çalışma masası");

        var audit = await _harness.QueryAsync(db => db.AuditEntries.Where(a => a.EntityId == id.Value.ToString()).ToListAsync());
        audit.ShouldAllBe(a => a.Action == AuditAction.Insert && a.EntityType == nameof(Widget) && a.UserId == WidgetHarness.UserId);
        audit.Single(a => a.Field == nameof(Widget.Name)).NewValue.ShouldBe("Masa");
        audit.Single(a => a.Field == nameof(Widget.Title)).NewValue!.ShouldContain("Çalışma masası");
        audit.Single(a => a.Field == nameof(Widget.Secret)).NewValue.ShouldBe(AuditLogInterceptor.MaskedValue);
        audit.ShouldNotContain(a => a.Field == nameof(Widget.CreatedAt));

        var outbox = await _harness.QueryAsync(db => db.OutboxMessages.SingleAsync());
        outbox.Type.ShouldBe(typeof(WidgetCreated).FullName);
        outbox.OccurredAt.ShouldBe(_harness.Time.GetUtcNow());
        outbox.ProcessedAt.ShouldBeNull();
        outbox.Payload.ShouldContain(id.Value.ToString());
        outbox.Payload.ShouldContain(WidgetHarness.Tenant.Id.ToString());
    }

    [Fact]
    public async Task Update_AuditsOldAndNewValues_AndSetsModifiedStamps()
    {
        var id = (await _harness.SendAsync(new CreateWidget("Masa"))).Value;
        _harness.Time.Advance(TimeSpan.FromHours(1));

        (await _harness.SendAsync(new RenameWidget(id, "Sandalye"))).IsSuccess.ShouldBeTrue();

        var widget = await _harness.QueryAsync(db => db.Widgets.SingleAsync(w => w.Id == id));
        widget.Name.ShouldBe("Sandalye");
        widget.ModifiedAt.ShouldBe(_harness.Time.GetUtcNow());
        widget.ModifiedBy.ShouldBe(WidgetHarness.UserId);

        var update = await _harness.QueryAsync(db => db.AuditEntries.SingleAsync(a => a.Action == AuditAction.Update));
        update.Field.ShouldBe(nameof(Widget.Name));
        update.OldValue.ShouldBe("Masa");
        update.NewValue.ShouldBe("Sandalye");
    }

    [Fact]
    public async Task ConcurrentUpdates_SecondCommitGetsConflict()
    {
        var id = (await _harness.SendAsync(new CreateWidget("Masa"))).Value;

        // The second writer loads the row before the first writer commits.
        await using var secondScope = _harness.CreateScope();
        var secondUnitOfWork = secondScope.ServiceProvider.GetRequiredService<ModuleUnitOfWork<WidgetDbContext>>();
        var staleWidget = await secondUnitOfWork.DbContext.Widgets.SingleAsync(w => w.Id == id, TestContext.Current.CancellationToken);

        (await _harness.SendAsync(new RenameWidget(id, "Birinci"))).IsSuccess.ShouldBeTrue();

        await secondUnitOfWork.BeginAsync(TestContext.Current.CancellationToken);
        staleWidget.ChangeSecret("ikinci");
        var result = await secondUnitOfWork.CommitAsync(TestContext.Current.CancellationToken);
        await secondUnitOfWork.RollbackAsync(TestContext.Current.CancellationToken);

        result.Error.ShouldBe(CommonErrors.ConcurrencyConflict);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        (await _harness.QueryAsync(db => db.Widgets.SingleAsync(w => w.Id == id))).Secret.ShouldBeNull();
    }

    [Fact]
    public async Task Delete_OfSoftDeletableEntity_HidesRow_AndAuditsDeletion()
    {
        var id = (await _harness.SendAsync(new CreateWidget("Masa"))).Value;

        (await _harness.SendAsync(new DeleteWidget(id))).IsSuccess.ShouldBeTrue();

        (await _harness.QueryAsync(db => db.Widgets.AnyAsync(w => w.Id == id))).ShouldBeFalse();
        var deleted = await _harness.QueryAsync(db => db.Widgets.IgnoreQueryFilters([ModuleDbContext.SoftDeleteFilter]).SingleAsync(w => w.Id == id));
        deleted.IsDeleted.ShouldBeTrue();
        deleted.DeletedBy.ShouldBe(WidgetHarness.UserId);
        deleted.DeletedAt.ShouldBe(_harness.Time.GetUtcNow());
        (await _harness.QueryAsync(db => db.AuditEntries.CountAsync(a => a.Action == AuditAction.Delete))).ShouldBe(1);
    }

    [Fact]
    public async Task HandlerException_RollsBackBusinessDataAndOutbox()
    {
        await Should.ThrowAsync<InvalidOperationException>(() => _harness.SendAsync(new CreateWidget("Masa", FailAfterSave: true)));

        (await _harness.QueryAsync(db => db.Widgets.IgnoreQueryFilters().CountAsync())).ShouldBe(0);
        (await _harness.QueryAsync(db => db.OutboxMessages.CountAsync())).ShouldBe(0);
        (await _harness.QueryAsync(db => db.AuditEntries.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task DomainEventHandler_ChangesAnotherAggregate_InTheSameTransaction()
    {
        var id = (await _harness.SendAsync(new CreateWidget("Masa"))).Value;

        await _harness.SendAsync(new RenameWidget(id, "Sandalye"));

        var log = await _harness.QueryAsync(db => db.WidgetLogs.SingleAsync());
        log.WidgetId.ShouldBe(id);
        log.Message.ShouldBe("Masa -> Sandalye");
    }

    [Fact]
    public async Task CommandChangingTwoAggregates_IsRejected()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(() => _harness.SendAsync(new CreateTwoWidgets()));

        exception.Message.ShouldContain("rule 7");
        (await _harness.QueryAsync(db => db.Widgets.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Conventions_MapToSnakeCaseColumns_JsonbAndXmin()
    {
        var columns = await _harness.QueryAsync(db => db.Database
            .SqlQueryRaw<ColumnInfo>(
                "SELECT column_name AS \"Name\", data_type AS \"Type\" FROM information_schema.columns WHERE table_schema = 'widgets' AND table_name = 'widgets'")
            .ToListAsync());

        columns.ShouldContain(c => c.Name == "title" && c.Type == "jsonb");
        columns.ShouldContain(c => c.Name == "created_by" && c.Type == "uuid");
        columns.ShouldContain(c => c.Name == "id" && c.Type == "uuid");
        columns.ShouldNotContain(c => c.Name == "version"); // xmin is a system column
    }

    public sealed record ColumnInfo(string Name, string Type);
}
