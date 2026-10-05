using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Merp.Testing.Containers;

/// <summary>
/// One PostgreSQL 18 container per test assembly. Register with
/// <c>[assembly: AssemblyFixture(typeof(PostgresFixture))]</c>; use <see cref="CreateDatabaseAsync"/> to give each
/// test class its own database.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string Image = "postgres:18";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image)
        .WithDatabase("postgres")
        .WithUsername("merp")
        .WithPassword("merp")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    /// <summary>Creates an empty database and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync(string? name = null)
    {
        name ??= "test_" + Guid.CreateVersion7().ToString("N");
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{name}\"";
        await command.ExecuteNonQueryAsync();

        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
