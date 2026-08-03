using Microsoft.Data.Sqlite;
using static Aneiang.Yarp.Storage.Sqlite.Migrations.MigrationHelper;

namespace Aneiang.Yarp.Storage.Sqlite.Migrations;

/// <summary>
/// Creates the <c>plugin_runtime_states</c> table for persisting plugin runtime
/// state (circuit breaker, rate-limit stats, WAF counts, retry counts, service
/// discovery status) separately from configuration data.
/// </summary>
internal sealed class Migration013_PluginRuntimeStates : ISchemaMigration
{
    public int Version => 13;
    public string Id => "013_plugin_runtime_states";
    public string Description => "Create plugin_runtime_states table for runtime state persistence";

    public async Task UpAsync(SqliteConnection conn, SqliteTransaction transaction, CancellationToken ct)
    {
        await ExecuteAsync(conn, transaction, """
            CREATE TABLE IF NOT EXISTS plugin_runtime_states (
                plugin_id    TEXT    NOT NULL,
                target_type  TEXT    NOT NULL,
                target_uid   TEXT    NOT NULL,
                node_id      TEXT    NOT NULL,
                state_json   TEXT    NOT NULL DEFAULT '{}',
                updated_at   TEXT    NOT NULL DEFAULT (datetime('now')),
                PRIMARY KEY (plugin_id, target_type, target_uid, node_id)
            ) WITHOUT ROWID;
            """, ct);

        await ExecuteAsync(conn, transaction, """
            CREATE INDEX IF NOT EXISTS idx_plugin_runtime_states_plugin
            ON plugin_runtime_states (plugin_id);
            """, ct);
    }
}
