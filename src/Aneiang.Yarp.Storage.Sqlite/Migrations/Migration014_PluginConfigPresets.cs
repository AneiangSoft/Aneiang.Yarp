using Microsoft.Data.Sqlite;
using static Aneiang.Yarp.Storage.Sqlite.Migrations.MigrationHelper;

namespace Aneiang.Yarp.Storage.Sqlite.Migrations;

/// <summary>
/// Creates the <c>plugin_config_presets</c> table for saving and reusing
/// plugin configuration presets (strategy presets).
/// </summary>
internal sealed class Migration014_PluginConfigPresets : ISchemaMigration
{
    public int Version => 14;
    public string Id => "014_plugin_config_presets";
    public string Description => "Create plugin_config_presets table for strategy presets";

    public async Task UpAsync(SqliteConnection conn, SqliteTransaction transaction, CancellationToken ct)
    {
        await ExecuteAsync(conn, transaction, """
            CREATE TABLE IF NOT EXISTS plugin_config_presets (
                id             TEXT    NOT NULL PRIMARY KEY,
                name           TEXT    NOT NULL,
                description    TEXT,
                plugin_id      TEXT    NOT NULL,
                config_json    TEXT    NOT NULL DEFAULT '{}',
                schema_version INTEGER NOT NULL DEFAULT 1,
                created_at     TEXT    NOT NULL DEFAULT (datetime('now')),
                updated_at     TEXT    NOT NULL DEFAULT (datetime('now'))
            ) WITHOUT ROWID;
            """, ct);

        await ExecuteAsync(conn, transaction, """
            CREATE INDEX IF NOT EXISTS idx_plugin_config_presets_plugin
            ON plugin_config_presets (plugin_id);
            """, ct);
    }
}
