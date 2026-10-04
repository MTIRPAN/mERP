using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;

namespace Merp.Infrastructure.Messaging.Integration;

/// <summary>
/// SQL for the outbox and inbox tables of a module schema. The schema name comes from code
/// (<c>IModuleSchema.SchemaName</c>) and is validated as an identifier; all values are passed as parameters.
/// </summary>
internal sealed partial record MessagingSql(string SelectPendingOutbox, string SelectNextInbox, string InsertInbox, string RecordInboxFailure)
{
    private static readonly ConcurrentDictionary<string, MessagingSql> Cache = new(StringComparer.Ordinal);

    public static MessagingSql For(string schema) => Cache.GetOrAdd(schema, Create);

    private static MessagingSql Create(string schema)
    {
        if (!Identifier().IsMatch(schema))
        {
            throw new ArgumentException($"'{schema}' is not a valid schema name.", nameof(schema));
        }

        var outbox = "\"" + schema + "\".outbox_messages";
        var inbox = "\"" + schema + "\".inbox_messages";
        const string pending = "processed_at IS NULL AND dead_lettered_at IS NULL AND (next_attempt_at IS NULL OR next_attempt_at <= {0})";

        return new MessagingSql(
            SelectPendingOutbox: "SELECT * FROM " + outbox + " WHERE " + pending + " ORDER BY occurred_at LIMIT {1} FOR UPDATE SKIP LOCKED",
            SelectNextInbox: "SELECT * FROM " + inbox + " WHERE " + pending + " ORDER BY received_at LIMIT 1 FOR UPDATE SKIP LOCKED",
            InsertInbox: "INSERT INTO " + inbox + " (message_id, handler, type, payload, received_at, trace_parent, attempt_count) "
                + "VALUES (@message_id, @handler, @type, @payload, @received_at, @trace_parent, 0) ON CONFLICT (message_id, handler) DO NOTHING",
            RecordInboxFailure: "UPDATE " + inbox + " SET attempt_count = @attempt_count, last_error = @last_error, "
                + "next_attempt_at = @next_attempt_at, dead_lettered_at = @dead_lettered_at WHERE message_id = @message_id AND handler = @handler");
    }

    /// <summary>Typed parameter; nulls keep their database type (EF cannot map a bare <see cref="DBNull"/>).</summary>
    public static NpgsqlParameter Parameter(string name, NpgsqlDbType type, object? value) =>
        new(name, type) { Value = value ?? DBNull.Value };

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
}
