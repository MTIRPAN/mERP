using Npgsql;
using Respawn;

namespace Merp.Testing.Database;

/// <summary>
/// Empties the tables of a test database between tests (Respawn) while keeping the schema and the migration
/// history, which is much faster than recreating the database.
/// </summary>
public sealed class DatabaseReset
{
    private readonly string _connectionString;
    private readonly Respawner _respawner;

    private DatabaseReset(string connectionString, Respawner respawner)
    {
        _connectionString = connectionString;
        _respawner = respawner;
    }

    public static async Task<DatabaseReset> CreateAsync(string connectionString, params string[] schemas)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = schemas,
            TablesToIgnore = ["__ef_migrations_history"],
        });
        return new DatabaseReset(connectionString, respawner);
    }

    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }
}
