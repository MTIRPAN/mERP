using Merp.BuildingBlocks.IntegrationTests.Persistence;
using Merp.SharedKernel.ValueObjects;
using Merp.Testing.Containers;
using Merp.Testing.Data;
using Merp.Testing.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Merp.BuildingBlocks.IntegrationTests.Testing;

public class TurkishDataTests
{
    [Fact]
    public void GeneratedIdentifiers_PassTheValueObjects()
    {
        for (var i = 0; i < 500; i++)
        {
            Tckn.Create(TurkishData.Tckn()).IsSuccess.ShouldBeTrue();
            Vkn.Create(TurkishData.Vkn()).IsSuccess.ShouldBeTrue();
            Iban.Create(TurkishData.Iban()).IsSuccess.ShouldBeTrue();
            PhoneNumber.Create(TurkishData.MobilePhone()).IsSuccess.ShouldBeTrue();
        }
    }

    [Fact]
    public void GeneratedEmails_PassTheValueObject() =>
        Email.Create(TurkishData.Email(TurkishData.FirstName(), TurkishData.LastName())).IsSuccess.ShouldBeTrue();
}

public class DatabaseResetTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Reset_EmptiesTables_ButKeepsSchemaAndMigrationHistory()
    {
        await using var harness = await WidgetHarness.CreateAsync(postgres);
        await harness.SendAsync(new CreateWidget("Masa"));
        var connectionString = await harness.QueryAsync(db => Task.FromResult(db.Database.GetConnectionString()!));
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var create = new NpgsqlCommand(
                "CREATE TABLE widgets.__ef_migrations_history (migration_id text PRIMARY KEY); INSERT INTO widgets.__ef_migrations_history VALUES ('Initial')",
                connection);
            await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var reset = await DatabaseReset.CreateAsync(connectionString, WidgetDbContext.SchemaName);
        await reset.ResetAsync();

        (await harness.QueryAsync(db => db.Widgets.IgnoreQueryFilters().CountAsync())).ShouldBe(0);
        (await harness.QueryAsync(db => db.OutboxMessages.CountAsync())).ShouldBe(0);
        (await harness.QueryAsync(db => db.AuditEntries.CountAsync())).ShouldBe(0);
        (await harness.QueryAsync(db => db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM widgets.__ef_migrations_history").SingleAsync())).ShouldBe(1);
    }
}
