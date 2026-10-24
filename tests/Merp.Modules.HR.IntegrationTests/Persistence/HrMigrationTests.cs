using Merp.Application.Abstractions.Context;
using Merp.Infrastructure;
using Merp.Infrastructure.Persistence;
using Merp.Modules.HR.Infrastructure;
using Merp.Testing.Containers;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Merp.Modules.HR.IntegrationTests.Persistence;

public class HrMigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrations_RunOnEmptyDatabase_AndKeepHistoryInModuleSchema()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMerpInfrastructure();
        services.AddHrInfrastructure();
        await using var provider = services.BuildServiceProvider();

        var migrator = provider.GetRequiredService<TenantDatabaseMigrator>();
        var tenant = new TenantInfo(Guid.CreateVersion7(), "test", "Test", "tr-TR");
        await migrator.MigrateAsync(tenant, connectionString, TestContext.Current.CancellationToken);
        // Running again must be a no-op
        await migrator.MigrateAsync(tenant, connectionString, TestContext.Current.CancellationToken);

        var tables = await ListAsync(connectionString, "SELECT table_name FROM information_schema.tables WHERE table_schema = @schema", "hr");
        tables.ShouldBe(
            ["__ef_migrations_history", "audit_entries", "inbox_messages", "outbox_messages", "number_counters", "stored_files", "org_units", "jobs", "positions", "reporting_lines",
                "employees", "employments", "job_assignments", "employee_sensitive_data", "sgk_exit_codes", "document_types", "employee_documents",
                "profile_change_requests", "leave_types", "leave_policies", "leave_policy_rule_sets"],
            ignoreOrder: true);
        (await ListAsync(connectionString, "SELECT table_name FROM information_schema.tables WHERE table_schema = @schema", "public")).ShouldBeEmpty();

        // The SGK leaving codes, the document types and the leave settings are seeded once (D38, D41)
        (await ListAsync(connectionString, "SELECT code FROM hr.sgk_exit_codes WHERE code = @schema", "03")).ShouldBe(["03"]);
        (await ListAsync(connectionString, "SELECT code FROM hr.document_types WHERE code = @schema", "HEALTH_REPORT")).ShouldBe(["HEALTH_REPORT"]);
        (await ListAsync(connectionString, "SELECT code FROM hr.leave_types WHERE code = @schema", "BABALIK")).ShouldBe(["BABALIK"]);
        (await ListAsync(connectionString, "SELECT code FROM hr.leave_policies WHERE code = @schema", "KANUNI")).ShouldBe(["KANUNI"]);

        // Unit paths are ltree with a GiST index (D37)
        (await ListAsync(connectionString, "SELECT extname FROM pg_extension WHERE extname = @schema", "ltree")).ShouldBe(["ltree"]);
        (await ListAsync(connectionString, "SELECT indexdef FROM pg_indexes WHERE schemaname = @schema AND indexname = 'ix_org_units_path'", "hr"))
            .ShouldHaveSingleItem().ShouldContain("USING gist (path)");
    }

    private static async Task<List<string>> ListAsync(string connectionString, string sql, string parameter)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schema", parameter);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var rows = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }
}
