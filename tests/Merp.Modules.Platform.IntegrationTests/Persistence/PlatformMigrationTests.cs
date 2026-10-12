using Merp.Application.Abstractions.Context;
using Merp.Infrastructure;
using Merp.Infrastructure.Persistence;
using Merp.Modules.Platform.Infrastructure;
using Merp.Testing.Containers;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Merp.Modules.Platform.IntegrationTests.Persistence;

public class PlatformMigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrations_RunOnEmptyDatabase_AndKeepHistoryInModuleSchema()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMerpInfrastructure();
        services.AddPlatformInfrastructure();
        await using var provider = services.BuildServiceProvider();

        var migrator = provider.GetRequiredService<TenantDatabaseMigrator>();
        var tenant = new TenantInfo(Guid.CreateVersion7(), "test", "Test", "tr-TR");
        await migrator.MigrateAsync(tenant, connectionString, TestContext.Current.CancellationToken);
        // Running again must be a no-op
        await migrator.MigrateAsync(tenant, connectionString, TestContext.Current.CancellationToken);

        var tables = await ListTablesAsync(connectionString, "platform");
        tables.ShouldBe(
            ["__ef_migrations_history", "ai_usage", "audit_entries", "inbox_messages", "outbox_messages", "companies", "branches", "provinces", "districts",
                "permissions", "roles", "role_permissions", "users", "role_assignments", "cultures", "localization_texts", "number_sequences", "number_counters", "custom_field_definitions", "stored_files", "notifications", "email_messages", "notification_templates", "holiday_calendars", "holidays", "work_schedules", "approval_definitions", "approval_requests", "approval_tasks", "approval_history", "delegations", "settings_profiles"],
            ignoreOrder: true);
        (await ListTablesAsync(connectionString, "public")).ShouldBeEmpty();

        // Reference data of Türkiye: 81 provinces and 973 districts (docs/reference-data.md)
        (await CountAsync(connectionString, "platform.provinces")).ShouldBe(81);
        (await CountAsync(connectionString, "platform.districts")).ShouldBe(973);
        (await CountAsync(connectionString, "platform.districts WHERE province_id = 34")).ShouldBe(39);

        // The seeder ran: permission catalog and the seven system roles (docs/domain/yetki-ve-onay.md §1.4)
        (await CountAsync(connectionString, "platform.permissions WHERE code = 'platform.roles.manage'")).ShouldBe(1);
        (await CountAsync(connectionString, "platform.roles WHERE is_system")).ShouldBe(7);
        (await CountAsync(connectionString, "platform.role_permissions WHERE permission_code = 'platform.users.manage'")).ShouldBe(1);

        // Only Turkish, active and default (PLAN P4)
        (await CountAsync(connectionString, "platform.cultures")).ShouldBe(1);
        (await CountAsync(connectionString, "platform.cultures WHERE code = 'tr-TR' AND is_default AND is_active")).ShouldBe(1);
    }

    private static async Task<long> CountAsync(string connectionString, string from)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {from}", connection);
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
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
