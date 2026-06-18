using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Storage;

/// <summary>
/// Runs all additive SQLite schema migrations in a single, deterministic place before repositories warm up.
/// All statements are idempotent and safe for existing databases.
/// </summary>
public sealed class SqliteSchemaMigrator : IHostedService
{
    private readonly SqliteConnectionFactory _connections;
    private readonly ILogger<SqliteSchemaMigrator> _logger;

    public SqliteSchemaMigrator(SqliteConnectionFactory connections, ILogger<SqliteSchemaMigrator> logger)
    {
        _connections = connections;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await CreateBaseTablesAsync(conn, cancellationToken);
        await ApplyColumnMigrationsAsync(conn, cancellationToken);
        await BackfillAsync(conn, cancellationToken);
        await CreateIndexesAsync(conn, cancellationToken);

        _logger.LogInformation("SQLite schema migration completed");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task CreateBaseTablesAsync(SqliteConnection conn, CancellationToken ct)
    {
        var sql = """
            CREATE TABLE IF NOT EXISTS yarp_clusters (
                cluster_id TEXT PRIMARY KEY,
                load_balancing_policy TEXT,
                health_check_config TEXT,
                circuit_breaker_config TEXT,
                source TEXT DEFAULT 'dynamic',
                created_by TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                last_heartbeat TEXT
            );
            CREATE TABLE IF NOT EXISTS yarp_routes (
                route_id TEXT PRIMARY KEY,
                cluster_id TEXT NOT NULL,
                match_path TEXT NOT NULL,
                "order" INTEGER DEFAULT 50,
                transforms TEXT,
                source TEXT DEFAULT 'dynamic',
                created_by TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                metadata TEXT
            );
            CREATE TABLE IF NOT EXISTS gateway_policies (
                policy_id TEXT PRIMARY KEY,
                policy_type TEXT NOT NULL DEFAULT 'route',
                display_name TEXT NOT NULL,
                description TEXT,
                enabled INTEGER DEFAULT 1,
                retry_config TEXT,
                rate_limit_config TEXT,
                circuit_breaker_config TEXT,
                waf_enabled TEXT,
                applied_targets TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS config_audit_logs (
                id TEXT PRIMARY KEY,
                action TEXT NOT NULL,
                target TEXT NOT NULL,
                target_type TEXT,
                operator TEXT,
                client_ip TEXT,
                before_data TEXT,
                after_data TEXT,
                success INTEGER DEFAULT 1,
                error_message TEXT,
                timestamp TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS proxy_logs (
                id TEXT PRIMARY KEY,
                method TEXT,
                path TEXT,
                route_id TEXT,
                cluster_id TEXT,
                destination_id TEXT,
                status_code INTEGER DEFAULT 0,
                duration_ms INTEGER DEFAULT 0,
                request_body_size INTEGER,
                response_body_size INTEGER,
                client_ip TEXT,
                error_message TEXT,
                timestamp TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS notification_history (
                id TEXT PRIMARY KEY,
                event_type TEXT NOT NULL,
                severity INTEGER DEFAULT 0,
                title TEXT NOT NULL,
                message TEXT NOT NULL,
                timestamp TEXT NOT NULL,
                cluster_id TEXT,
                route_id TEXT,
                client_ip TEXT,
                block_reason TEXT,
                request_uri TEXT,
                error_message TEXT,
                attempt_count INTEGER,
                last_status_code INTEGER,
                notified_channels TEXT,
                delivery_success INTEGER DEFAULT 1
            );
            """;
        await ExecuteAsync(conn, sql, ct);
    }

    private static async Task ApplyColumnMigrationsAsync(SqliteConnection conn, CancellationToken ct)
    {
        var migrations = new[]
        {
            "ALTER TABLE yarp_clusters ADD COLUMN cluster_uid TEXT",
            "ALTER TABLE yarp_clusters ADD COLUMN circuit_breaker_config TEXT",
            "ALTER TABLE yarp_routes ADD COLUMN route_uid TEXT",
            "ALTER TABLE yarp_routes ADD COLUMN cluster_uid TEXT",
            "ALTER TABLE gateway_policies ADD COLUMN policy_uid TEXT",
            "ALTER TABLE gateway_policies ADD COLUMN policy_type TEXT NOT NULL DEFAULT 'route'",
            "ALTER TABLE gateway_policies ADD COLUMN waf_enabled TEXT",
            "ALTER TABLE gateway_policies ADD COLUMN applied_targets TEXT",
            "ALTER TABLE config_audit_logs ADD COLUMN target_uid TEXT",
            "ALTER TABLE config_audit_logs ADD COLUMN target_key_snapshot TEXT",
            "ALTER TABLE config_audit_logs ADD COLUMN target_display_name_snapshot TEXT",
            "ALTER TABLE proxy_logs ADD COLUMN route_uid TEXT",
            "ALTER TABLE proxy_logs ADD COLUMN route_key_snapshot TEXT",
            "ALTER TABLE proxy_logs ADD COLUMN cluster_uid TEXT",
            "ALTER TABLE proxy_logs ADD COLUMN cluster_key_snapshot TEXT",
            "ALTER TABLE proxy_logs ADD COLUMN destination_uid TEXT",
            "ALTER TABLE proxy_logs ADD COLUMN destination_key_snapshot TEXT",
            "ALTER TABLE notification_history ADD COLUMN cluster_uid TEXT",
            "ALTER TABLE notification_history ADD COLUMN cluster_key_snapshot TEXT",
            "ALTER TABLE notification_history ADD COLUMN route_uid TEXT",
            "ALTER TABLE notification_history ADD COLUMN route_key_snapshot TEXT"
        };

        foreach (var migration in migrations)
        {
            try { await ExecuteAsync(conn, migration, ct); }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
        }
    }

    private static async Task BackfillAsync(SqliteConnection conn, CancellationToken ct)
    {
        var statements = new[]
        {
            "UPDATE yarp_clusters SET cluster_uid = lower(hex(randomblob(16))) WHERE cluster_uid IS NULL OR cluster_uid = ''",
            "UPDATE yarp_routes SET route_uid = lower(hex(randomblob(16))) WHERE route_uid IS NULL OR route_uid = ''",
            "UPDATE yarp_routes SET cluster_uid = (SELECT cluster_uid FROM yarp_clusters WHERE yarp_clusters.cluster_id = yarp_routes.cluster_id) WHERE cluster_uid IS NULL OR cluster_uid = ''",
            "UPDATE gateway_policies SET policy_uid = lower(hex(randomblob(16))) WHERE policy_uid IS NULL OR policy_uid = ''",
            "UPDATE config_audit_logs SET target_key_snapshot = target WHERE target_key_snapshot IS NULL OR target_key_snapshot = ''",
            "UPDATE proxy_logs SET route_key_snapshot = COALESCE(route_key_snapshot, route_id), cluster_key_snapshot = COALESCE(cluster_key_snapshot, cluster_id), destination_key_snapshot = COALESCE(destination_key_snapshot, destination_id) WHERE route_key_snapshot IS NULL OR cluster_key_snapshot IS NULL OR destination_key_snapshot IS NULL",
            "UPDATE notification_history SET cluster_key_snapshot = COALESCE(cluster_key_snapshot, cluster_id), route_key_snapshot = COALESCE(route_key_snapshot, route_id) WHERE cluster_key_snapshot IS NULL OR route_key_snapshot IS NULL"
        };

        foreach (var statement in statements)
            await ExecuteAsync(conn, statement, ct);
    }

    private static async Task CreateIndexesAsync(SqliteConnection conn, CancellationToken ct)
    {
        var sql = """
            CREATE UNIQUE INDEX IF NOT EXISTS ix_clusters_uid ON yarp_clusters(cluster_uid);
            CREATE UNIQUE INDEX IF NOT EXISTS ix_routes_uid ON yarp_routes(route_uid);
            CREATE INDEX IF NOT EXISTS ix_routes_cluster_uid ON yarp_routes(cluster_uid);
            CREATE UNIQUE INDEX IF NOT EXISTS ix_policies_uid ON gateway_policies(policy_uid);
            CREATE TABLE IF NOT EXISTS policy_targets (
                id TEXT PRIMARY KEY,
                policy_uid TEXT NOT NULL,
                policy_id TEXT NOT NULL,
                target_type TEXT NOT NULL,
                target_uid TEXT NOT NULL,
                target_key_snapshot TEXT NOT NULL,
                created_at TEXT NOT NULL,
                UNIQUE(policy_uid, target_type, target_uid)
            );
            CREATE INDEX IF NOT EXISTS ix_policy_targets_policy ON policy_targets(policy_id, target_type);
            CREATE INDEX IF NOT EXISTS ix_policy_targets_target ON policy_targets(target_type, target_uid);
            """;
        await ExecuteAsync(conn, sql, ct);
    }

    private static async Task ExecuteAsync(SqliteConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
