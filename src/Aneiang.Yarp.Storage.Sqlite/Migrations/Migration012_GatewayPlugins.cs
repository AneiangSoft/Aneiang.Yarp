using Microsoft.Data.Sqlite;
using static Aneiang.Yarp.Storage.Sqlite.Migrations.MigrationHelper;

namespace Aneiang.Yarp.Storage.Sqlite.Migrations;

/// <summary>Adds durable plugin installation state and stable binding identity/version columns.</summary>
internal sealed class Migration012_GatewayPlugins : ISchemaMigration
{
    public int Version => 12;
    public string Id => "012_gateway_plugins";
    public string Description => "Persist gateway plugins and stable plugin binding identities";

    public async Task UpAsync(SqliteConnection conn, SqliteTransaction transaction, CancellationToken ct)
    {
        await ExecuteAsync(conn, transaction, """
            CREATE TABLE IF NOT EXISTS gateway_plugins (
                plugin_id TEXT PRIMARY KEY,
                version TEXT NOT NULL,
                enabled INTEGER NOT NULL DEFAULT 1,
                is_built_in INTEGER NOT NULL DEFAULT 0,
                source_path TEXT NULL,
                registration_status TEXT NOT NULL DEFAULT 'installed',
                last_error TEXT NULL,
                installed_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """, ct);

        await AddColumnIfNotExistsAsync(conn, transaction, "plugin_bindings", "plugin_version", "TEXT NOT NULL DEFAULT ''", ct);
        await AddColumnIfNotExistsAsync(conn, transaction, "plugin_bindings", "route_uid", "TEXT NULL", ct);
        await AddColumnIfNotExistsAsync(conn, transaction, "plugin_bindings", "cluster_uid", "TEXT NULL", ct);

        // Existing databases stored the mutable RouteId/ClusterId in scope_id. Resolve stable
        // identities where possible while retaining scope_id as a compatibility fallback.
        await ExecuteAsync(conn, transaction, """
            UPDATE plugin_bindings
            SET route_uid = COALESCE(
                (SELECT route_uid FROM yarp_routes WHERE yarp_routes.route_id = plugin_bindings.scope_id LIMIT 1),
                scope_id)
            WHERE scope = 1 AND (route_uid IS NULL OR route_uid = '');

            UPDATE plugin_bindings
            SET cluster_uid = COALESCE(
                (SELECT cluster_uid FROM yarp_clusters WHERE yarp_clusters.cluster_id = plugin_bindings.scope_id LIMIT 1),
                scope_id)
            WHERE scope = 2 AND (cluster_uid IS NULL OR cluster_uid = '');
            """, ct);
    }
}
