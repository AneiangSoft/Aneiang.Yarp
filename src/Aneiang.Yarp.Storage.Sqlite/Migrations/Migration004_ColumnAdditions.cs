using Microsoft.Data.Sqlite;
using static Aneiang.Yarp.Storage.Sqlite.Migrations.MigrationHelper;

namespace Aneiang.Yarp.Storage.Sqlite.Migrations;

/// <summary>Add UID and snapshot columns to existing tables (idempotent).</summary>
internal sealed class Migration004_ColumnAdditions : ISchemaMigration
{
    public int Version => 4;
    public string Id => "004_uid_snapshot_columns";
    public string Description => "Add UID and snapshot columns to all tables";

    public async Task UpAsync(SqliteConnection conn, SqliteTransaction transaction, CancellationToken ct)
    {
        var columns = new (string table, string column, string definition)[]
        {
            ("yarp_clusters", "cluster_uid", "TEXT"),
            ("yarp_clusters", "circuit_breaker_config", "TEXT"),
            ("yarp_clusters", "config_json", "TEXT"),
            ("yarp_routes", "route_uid", "TEXT"),
            ("yarp_routes", "cluster_uid", "TEXT"),
            ("yarp_routes", "config_json", "TEXT"),
            ("gateway_policies", "policy_uid", "TEXT"),
            ("gateway_policies", "policy_type", "TEXT"),
            ("gateway_policies", "waf_enabled", "TEXT"),
            ("gateway_policies", "applied_targets", "TEXT"),
            ("config_audit_logs", "target_uid", "TEXT"),
            ("config_audit_logs", "target_key_snapshot", "TEXT"),
            ("config_audit_logs", "target_display_name_snapshot", "TEXT"),
            ("config_audit_logs", "target_type", "TEXT"),
            ("proxy_logs", "route_uid", "TEXT"),
            ("proxy_logs", "route_key_snapshot", "TEXT"),
            ("proxy_logs", "cluster_uid", "TEXT"),
            ("proxy_logs", "cluster_key_snapshot", "TEXT"),
            ("proxy_logs", "destination_uid", "TEXT"),
            ("proxy_logs", "destination_key_snapshot", "TEXT"),
            ("notification_history", "cluster_uid", "TEXT"),
            ("notification_history", "cluster_key_snapshot", "TEXT"),
            ("notification_history", "route_uid", "TEXT"),
            ("notification_history", "route_key_snapshot", "TEXT"),
            ("proxy_logs_meta", "EventType", "TEXT NOT NULL DEFAULT ''"),
            ("proxy_logs_meta", "StatusCode", "INTEGER DEFAULT 0"),
            ("proxy_logs_meta", "ElapsedMs", "REAL DEFAULT 0"),
            ("proxy_logs_meta", "HasRequestBody", "INTEGER DEFAULT 0"),
            ("proxy_logs_meta", "HasResponseBody", "INTEGER DEFAULT 0"),
            ("proxy_logs_meta", "DownstreamUrl", "TEXT"),
        };

        foreach (var (table, column, definition) in columns)
            await AddColumnIfNotExistsAsync(conn, transaction, table, column, definition, ct);

        // Rebuild yarp_destinations with composite PK if old single-PK exists
        var createSql = await ExecuteScalarStringAsync(conn, transaction,
            "SELECT sql FROM sqlite_master WHERE type='table' AND name='yarp_destinations'", ct);
        if (createSql != null && !createSql.Contains("PRIMARY KEY (cluster_id, destination_id)", StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteAsync(conn, transaction, "ALTER TABLE yarp_destinations RENAME TO yarp_destinations_old", ct);
            await ExecuteAsync(conn, transaction, """
                CREATE TABLE yarp_destinations (
                    destination_id TEXT NOT NULL,
                    cluster_id TEXT NOT NULL,
                    address TEXT NOT NULL,
                    host TEXT,
                    healthy INTEGER DEFAULT 1,
                    metadata TEXT,
                    PRIMARY KEY (cluster_id, destination_id),
                    FOREIGN KEY (cluster_id) REFERENCES yarp_clusters(cluster_id) ON DELETE CASCADE
                )
                """, ct);
            await ExecuteAsync(conn, transaction, """
                INSERT INTO yarp_destinations (destination_id, cluster_id, address, host, healthy, metadata)
                SELECT destination_id, cluster_id, address, host, healthy, metadata
                FROM yarp_destinations_old
                """, ct);
            await ExecuteAsync(conn, transaction, "DROP TABLE yarp_destinations_old", ct);
        }
    }
}
