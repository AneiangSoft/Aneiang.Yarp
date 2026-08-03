using Microsoft.Data.Sqlite;
using static Aneiang.Yarp.Storage.Sqlite.Migrations.MigrationHelper;

namespace Aneiang.Yarp.Storage.Sqlite.Migrations;

/// <summary>Plugin schemas and route/cluster configuration bindings.</summary>
internal sealed class Migration010_PluginConfiguration : ISchemaMigration
{
    public int Version => 10;
    public string Id => "010_plugin_configuration";
    public string Description => "Create plugin schema and binding tables";

    public Task UpAsync(SqliteConnection conn, SqliteTransaction transaction, CancellationToken ct)
        => ExecuteAsync(conn, transaction, """
            CREATE TABLE IF NOT EXISTS plugin_schemas (
                plugin_id TEXT NOT NULL,
                schema_version INTEGER NOT NULL,
                schema_json TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                PRIMARY KEY (plugin_id, schema_version)
            );

            CREATE TABLE IF NOT EXISTS plugin_bindings (
                id TEXT PRIMARY KEY,
                plugin_id TEXT NOT NULL,
                scope INTEGER NOT NULL CHECK (scope IN (1, 2)),
                scope_id TEXT NOT NULL,
                enabled INTEGER NOT NULL DEFAULT 1,
                config_json TEXT NOT NULL,
                schema_version INTEGER NOT NULL DEFAULT 1,
                config_version INTEGER NOT NULL DEFAULT 1,
                sort_order INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                UNIQUE (plugin_id, scope, scope_id)
            );

            CREATE INDEX IF NOT EXISTS ix_plugin_bindings_scope ON plugin_bindings(scope, scope_id);
            CREATE INDEX IF NOT EXISTS ix_plugin_bindings_plugin ON plugin_bindings(plugin_id);
            """, ct);
}
