using Merp.Application.Abstractions.Context;
using Merp.Infrastructure;
using Merp.Infrastructure.Persistence;
using Merp.Modules.Recruitment.Infrastructure;
using Merp.Testing.Containers;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Merp.Modules.Recruitment.IntegrationTests.Persistence;

public class RecruitmentMigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrations_RunOnEmptyDatabase_AndKeepHistoryInModuleSchema()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMerpInfrastructure();
        services.AddRecruitmentInfrastructure();
        await using var provider = services.BuildServiceProvider();

        var migrator = provider.GetRequiredService<TenantDatabaseMigrator>();
        var tenant = new TenantInfo(Guid.CreateVersion7(), "test", "Test", "tr-TR");
        await migrator.MigrateAsync(tenant, connectionString, TestContext.Current.CancellationToken);
        // Running again must be a no-op
        await migrator.MigrateAsync(tenant, connectionString, TestContext.Current.CancellationToken);

        var tables = await ListTablesAsync(connectionString, "recruitment");
        tables.ShouldBe(["__ef_migrations_history", "audit_entries", "inbox_messages", "outbox_messages", "number_counters", "stored_files", "stage_templates", "lookups", "consent_texts", "job_requisitions"], ignoreOrder: true);
        (await ListTablesAsync(connectionString, "public")).ShouldBeEmpty();
    }

    private static async Task<List<string>> ListTablesAsync(string connectionString, string schema)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT table_name FROM information_schema.tables WHERE table_schema = @schema", connection);
        command.Parameters.AddWithValue("schema", schema);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var tables = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }
}
