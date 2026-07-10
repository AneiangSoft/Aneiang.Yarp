using Microsoft.Data.Sqlite;
using static Aneiang.Yarp.Storage.Sqlite.Migrations.MigrationHelper;

namespace Aneiang.Yarp.Storage.Sqlite.Migrations;

/// <summary>Core YARP tables: clusters, routes, destinations.</summary>
internal sealed class Migration001_CoreTables : ISchemaMigration
{
    public int Version => 1;
    public string Id => "001_core_tables";
    public string Description => "Create core YARP tables (clusters, routes, destinations)";

    public Task UpAsync(SqliteConnection conn, SqliteTransaction transaction, CancellationToken ct)
        => ExecuteAsync(conn, transaction, """
            CREATE TABLE IF NOT EXISTS yarp_clusters (
                cluster_id TEXT PRIMARY KEY,
                load_balancing_policy TEXT,
                health_check_config TEXT,
                circuit_breaker_config TEXT,
                source TEXT DEFAULT 'dynamic',
                created_by TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                last_heartbeat TEXT,
                config_json TEXT
            );
            CREATE TABLE IF NOT EXISTS yarp_routes (
                route_id TEXT PRIMARY KEY,
                cluster_id TEXT NOT NULL,
                match_path TEXT NOT NULL,
                "order" INTEGER DEFAULT 2147483647,
                transforms TEXT,
                source TEXT DEFAULT 'dynamic',
                created_by TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                metadata TEXT,
                config_json TEXT
            );
            CREATE TABLE IF NOT EXISTS yarp_destinations (
                destination_id TEXT NOT NULL,
                cluster_id TEXT NOT NULL,
                address TEXT NOT NULL,
                host TEXT,
                healthy INTEGER DEFAULT 1,
                metadata TEXT,
                PRIMARY KEY (cluster_id, destination_id),
                FOREIGN KEY (cluster_id) REFERENCES yarp_clusters(cluster_id) ON DELETE CASCADE
            );
            """, ct);
}
