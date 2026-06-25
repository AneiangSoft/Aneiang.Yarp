using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Storage.Sqlite;

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

        // Reduce "database is locked" stalls during concurrent access / recovery.
        await ExecuteAsync(conn, "PRAGMA busy_timeout=30000;", cancellationToken);

        await EnsureMigrationTableAsync(conn, cancellationToken);

        // Phase 1: schema (tables, columns, indexes) — must succeed.
        await RunSchemaMigrationAsync(
            conn,
            "20260618_001_enterprise_identity_and_history_schema",
            "Enterprise identity UID, policy targets and history snapshots",
            cancellationToken);

        await RunSchemaMigrationAsync(
            conn,
            "20260619_001_storage_schema_centralization",
            "Centralize repository-owned SQLite tables and indexes in schema migrator",
            cancellationToken);

        await RunSchemaMigrationAsync(
            conn,
            "20260622_001_destination_composite_pk",
            "Change yarp_destinations primary key to (cluster_id, destination_id) so destinations are unique per cluster",
            cancellationToken);

        await RunSchemaMigrationAsync(
            conn,
            "20260625_001_full_yarp_config_json",
            "Add config_json columns to yarp_routes and yarp_clusters to carry all native YARP properties",
            cancellationToken);

        // Phase 2: data backfill — best-effort, time-boxed so startup doesn't hang.
        var backfillCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        backfillCts.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await RunDataBackfillAsync(conn, backfillCts.Token);
        }
        catch (OperationCanceledException) when (backfillCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "SQLite data backfill exceeded 30s time limit; remaining rows will be backfilled on next startup");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RunSchemaMigrationAsync(SqliteConnection conn, string migrationId, string description, CancellationToken ct)
    {
        if (await IsMigrationAppliedAsync(conn, migrationId, ct))
            return;

        _logger.LogInformation("SQLite schema migration running: {MigrationId}", migrationId);
        await using var transaction = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await CreateBaseTablesAsync(conn, transaction, ct);
            await ApplyColumnMigrationsAsync(conn, transaction, ct);

            // Migration 20260622_001: rebuild yarp_destinations with composite PK
            if (migrationId == "20260622_001_destination_composite_pk")
            {
                await RebuildDestinationsTableAsync(conn, transaction, ct);
            }

            await CreateIndexesAsync(conn, transaction, ct);
            await MarkMigrationAppliedAsync(conn, transaction, migrationId, description, ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            try { await transaction.RollbackAsync(CancellationToken.None); }
            catch (Exception rollbackEx) { _logger.LogWarning(rollbackEx, "Failed to roll back SQLite schema migration transaction"); }

            _logger.LogError(ex, "SQLite schema migration failed and was rolled back: {MigrationId}", migrationId);
            throw;
        }

        _logger.LogInformation("SQLite schema migration completed: {MigrationId}", migrationId);
    }

    private async Task RunDataBackfillAsync(SqliteConnection conn, CancellationToken ct)
    {
        await BackfillInBatchesAsync(conn, "yarp_clusters",
            "cluster_uid = lower(hex(randomblob(16)))",
            "cluster_uid IS NULL OR cluster_uid = ''", ct);

        await BackfillInBatchesAsync(conn, "yarp_routes",
            "route_uid = lower(hex(randomblob(16)))",
            "route_uid IS NULL OR route_uid = ''", ct);

        await BackfillInBatchesAsync(conn, "yarp_routes",
            "cluster_uid = (SELECT cluster_uid FROM yarp_clusters WHERE yarp_clusters.cluster_id = yarp_routes.cluster_id)",
            "(cluster_uid IS NULL OR cluster_uid = '') AND cluster_id IS NOT NULL", ct);

        await BackfillInBatchesAsync(conn, "gateway_policies",
            "policy_uid = lower(hex(randomblob(16)))",
            "policy_uid IS NULL OR policy_uid = ''", ct);

        await BackfillInBatchesAsync(conn, "config_audit_logs",
            "target_key_snapshot = target",
            "(target_key_snapshot IS NULL OR target_key_snapshot = '') AND target IS NOT NULL AND target <> ''", ct);

        await BackfillInBatchesAsync(conn, "proxy_logs",
            "route_key_snapshot = COALESCE(route_key_snapshot, route_id), cluster_key_snapshot = COALESCE(cluster_key_snapshot, cluster_id), destination_key_snapshot = COALESCE(destination_key_snapshot, destination_id)",
            "(route_key_snapshot IS NULL AND route_id IS NOT NULL) OR (cluster_key_snapshot IS NULL AND cluster_id IS NOT NULL) OR (destination_key_snapshot IS NULL AND destination_id IS NOT NULL)", ct);

        await BackfillInBatchesAsync(conn, "notification_history",
            "cluster_key_snapshot = COALESCE(cluster_key_snapshot, cluster_id), route_key_snapshot = COALESCE(route_key_snapshot, route_id)",
            "(cluster_key_snapshot IS NULL AND cluster_id IS NOT NULL) OR (route_key_snapshot IS NULL AND route_id IS NOT NULL)", ct);

        await using (var transaction = (SqliteTransaction)await conn.BeginTransactionAsync(ct))
        {
            await ExecuteAsync(conn, transaction, """
                INSERT INTO notification_settings (id, enabled, updated_at)
                VALUES ('notification_settings', 1, datetime('now'))
                ON CONFLICT(id) DO NOTHING
                """, ct);

            await MigrateAppliedTargetsAsync(conn, transaction, ct);
            await transaction.CommitAsync(ct);
        }

        _logger.LogInformation("SQLite data backfill completed");
    }

    private static async Task EnsureMigrationTableAsync(SqliteConnection conn, CancellationToken ct)
    {
        await ExecuteAsync(conn, """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                id TEXT PRIMARY KEY,
                description TEXT NOT NULL,
                checksum TEXT,
                applied_at TEXT NOT NULL
            );
            """, ct);
    }

    private static async Task<bool> IsMigrationAppliedAsync(SqliteConnection conn, string migrationId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", migrationId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    private static async Task MarkMigrationAppliedAsync(SqliteConnection conn, SqliteTransaction transaction, string migrationId, string description, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandTimeout = 30;
        cmd.CommandText = """
            INSERT INTO schema_migrations (id, description, checksum, applied_at)
            VALUES (@id, @desc, @checksum, @at)
            ON CONFLICT(id) DO NOTHING
            """;
        cmd.Parameters.AddWithValue("@id", migrationId);
        cmd.Parameters.AddWithValue("@desc", description);
        cmd.Parameters.AddWithValue("@checksum", DBNull.Value);
        cmd.Parameters.AddWithValue("@at", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task CreateBaseTablesAsync(SqliteConnection conn, SqliteTransaction transaction, CancellationToken ct)
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
                last_heartbeat TEXT,
                config_json TEXT
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
            CREATE TABLE IF NOT EXISTS yarp_config_history (
                version_id TEXT PRIMARY KEY,
                description TEXT,
                client_ip TEXT,
                config_data TEXT NOT NULL,
                diff_data TEXT,
                created_by TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS waf_settings (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                enabled INTEGER DEFAULT 0,
                enable_ip_check INTEGER DEFAULT 1,
                ip_whitelist_json TEXT,
                ip_blacklist_json TEXT,
                enable_request_size_validation INTEGER DEFAULT 1,
                max_request_body_size INTEGER DEFAULT 10485760,
                max_header_count INTEGER DEFAULT 64,
                max_header_size INTEGER DEFAULT 8192,
                enable_sql_injection_detection INTEGER DEFAULT 1,
                enable_xss_detection INTEGER DEFAULT 1,
                enable_path_traversal_detection INTEGER DEFAULT 1,
                extra_script_sources TEXT,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS notification_settings (
                id TEXT PRIMARY KEY DEFAULT 'notification_settings',
                enabled INTEGER DEFAULT 1,
                channels TEXT,
                rules TEXT,
                global_settings TEXT,
                updated_at TEXT NOT NULL
            );
            """;
        await ExecuteAsync(conn, transaction, sql, ct);
    }

    private static async Task ApplyColumnMigrationsAsync(SqliteConnection conn, SqliteTransaction transaction, CancellationToken ct)
    {
        var migrations = new[]
        {
            ("yarp_clusters", "cluster_uid"),
            ("yarp_clusters", "circuit_breaker_config"),
            ("yarp_clusters", "config_json"),
            ("yarp_routes", "route_uid"),
            ("yarp_routes", "cluster_uid"),
            ("yarp_routes", "config_json"),
            ("gateway_policies", "policy_uid"),
            ("gateway_policies", "policy_type"),
            ("gateway_policies", "waf_enabled"),
            ("gateway_policies", "applied_targets"),
            ("config_audit_logs", "target_uid"),
            ("config_audit_logs", "target_key_snapshot"),
            ("config_audit_logs", "target_display_name_snapshot"),
            ("proxy_logs", "route_uid"),
            ("proxy_logs", "route_key_snapshot"),
            ("proxy_logs", "cluster_uid"),
            ("proxy_logs", "cluster_key_snapshot"),
            ("proxy_logs", "destination_uid"),
            ("proxy_logs", "destination_key_snapshot"),
            ("notification_history", "cluster_uid"),
            ("notification_history", "cluster_key_snapshot"),
            ("notification_history", "route_uid"),
            ("notification_history", "route_key_snapshot")
        };

        foreach (var (table, column) in migrations)
        {
            if (!await ColumnExistsAsync(conn, transaction, table, column, ct))
            {
                await ExecuteAsync(conn, transaction, $"ALTER TABLE {table} ADD COLUMN {column} TEXT", ct);
            }
        }
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection conn, SqliteTransaction transaction, string table, string column, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info(@table) WHERE name = @column";
        cmd.Parameters.AddWithValue("@table", table);
        cmd.Parameters.AddWithValue("@column", column);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    private static async Task MigrateAppliedTargetsAsync(SqliteConnection conn, SqliteTransaction transaction, CancellationToken ct)
    {
        await using var countCmd = conn.CreateCommand();
        countCmd.Transaction = transaction;
        countCmd.CommandTimeout = 30;
        countCmd.CommandText = "SELECT COUNT(*) FROM policy_targets";
        var existingCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));
        if (existingCount > 0) return;

        var policies = new List<(string PolicyUid, string PolicyId, string PolicyType, string? AppliedTargets, DateTime UpdatedAt)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandTimeout = 30;
            cmd.CommandText = """
                SELECT policy_uid, policy_id, policy_type, applied_targets, updated_at
                FROM gateway_policies
                WHERE applied_targets IS NOT NULL AND applied_targets <> ''
                """;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                policies.Add((
                    reader.IsDBNull(0) ? Guid.NewGuid().ToString("N") : reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? "route" : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    DateTime.TryParse(reader.IsDBNull(4) ? null : reader.GetString(4), out var updatedAt) ? updatedAt : DateTime.UtcNow));
            }
        }

        foreach (var policy in policies)
        {
            List<string>? targets;
            try { targets = JsonSerializer.Deserialize<List<string>>(policy.AppliedTargets ?? "[]"); }
            catch { continue; }
            if (targets == null) continue;

            foreach (var targetKey in targets.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await using var insertCmd = conn.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandTimeout = 30;
                insertCmd.CommandText = """
                    INSERT OR IGNORE INTO policy_targets (id, policy_uid, policy_id, target_type, target_uid, target_key_snapshot, created_at)
                    VALUES (@id, @puid, @pid, @type, @tuid, @tkey, @ca)
                    """;
                insertCmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                insertCmd.Parameters.AddWithValue("@puid", policy.PolicyUid);
                insertCmd.Parameters.AddWithValue("@pid", policy.PolicyId);
                insertCmd.Parameters.AddWithValue("@type", policy.PolicyType);
                insertCmd.Parameters.AddWithValue("@tuid", StableUidFromKey(policy.PolicyType, targetKey));
                insertCmd.Parameters.AddWithValue("@tkey", targetKey);
                insertCmd.Parameters.AddWithValue("@ca", policy.UpdatedAt.ToString("O"));
                await insertCmd.ExecuteNonQueryAsync(ct);
            }
        }
    }

    private static async Task RebuildDestinationsTableAsync(SqliteConnection conn, SqliteTransaction transaction, CancellationToken ct)
    {
        // Check if the old single-PK table exists and needs rebuilding
        var createSql = await ExecuteScalarStringAsync(conn, transaction,
            "SELECT sql FROM sqlite_master WHERE type='table' AND name='yarp_destinations'", ct);

        // Table doesn't exist yet — CreateBaseTablesAsync will create it with the correct schema
        if (createSql == null) return;

        // If the table already has composite PK, skip
        if (createSql.Contains("PRIMARY KEY (cluster_id, destination_id)", StringComparison.OrdinalIgnoreCase))
            return;

        // Rebuild: rename old, create new with composite PK, copy data, drop old
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

    private static async Task CreateIndexesAsync(SqliteConnection conn, SqliteTransaction transaction, CancellationToken ct)
    {
        var sql = """
            CREATE INDEX IF NOT EXISTS ix_routes_cluster ON yarp_routes(cluster_id);
            CREATE INDEX IF NOT EXISTS ix_destinations_cluster ON yarp_destinations(cluster_id);
            CREATE INDEX IF NOT EXISTS ix_audit_timestamp ON config_audit_logs(timestamp DESC);
            CREATE INDEX IF NOT EXISTS ix_audit_target ON config_audit_logs(target);
            CREATE INDEX IF NOT EXISTS ix_proxy_logs_timestamp ON proxy_logs(timestamp DESC);
            CREATE INDEX IF NOT EXISTS ix_config_history_created ON yarp_config_history(created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_notif_history_timestamp ON notification_history(timestamp DESC);
            CREATE INDEX IF NOT EXISTS ix_notif_history_type ON notification_history(event_type);
            CREATE INDEX IF NOT EXISTS ix_policies_enabled ON gateway_policies(enabled);
            CREATE INDEX IF NOT EXISTS ix_policies_type ON gateway_policies(policy_type);
            CREATE UNIQUE INDEX IF NOT EXISTS ix_clusters_uid ON yarp_clusters(cluster_uid);
            CREATE UNIQUE INDEX IF NOT EXISTS ix_routes_uid ON yarp_routes(route_uid);
            CREATE INDEX IF NOT EXISTS ix_routes_cluster_uid ON yarp_routes(cluster_uid);
            CREATE UNIQUE INDEX IF NOT EXISTS ix_policies_uid ON gateway_policies(policy_uid);
            CREATE INDEX IF NOT EXISTS ix_policy_targets_policy ON policy_targets(policy_id, target_type);
            CREATE INDEX IF NOT EXISTS ix_policy_targets_target ON policy_targets(target_type, target_uid);
            """;
        await ExecuteAsync(conn, transaction, sql, ct);
    }

    private static string StableUidFromKey(string prefix, string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(prefix + ":" + key));
        return Convert.ToHexString(bytes, 0, 16).ToLowerInvariant();
    }

    private async Task BackfillInBatchesAsync(
        SqliteConnection conn,
        string table,
        string setClause,
        string whereClause,
        CancellationToken ct)
    {
        var countSql = $"SELECT COUNT(*) FROM {table} WHERE {whereClause}";
        var rowsToFix = await ExecuteScalarAsync(conn, countSql, ct);
        if (rowsToFix <= 0) return;

        _logger.LogInformation("SQLite backfill starting for {Table}: {RowsToFix} rows", table, rowsToFix);

        const int BatchSize = 1000;
        const int MaxBatches = 100000;
        long totalAffected = 0;
        int batch = 0;

        while (!ct.IsCancellationRequested && batch < MaxBatches)
        {
            await using var transaction = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
            int affected;
            try
            {
                affected = await ExecuteAsync(conn, transaction, $"""
                    UPDATE {table}
                    SET {setClause}
                    WHERE rowid IN (
                        SELECT rowid FROM {table}
                        WHERE {whereClause}
                        LIMIT {BatchSize}
                    )
                    """, ct);
                await transaction.CommitAsync(ct);
            }
            catch
            {
                try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
                throw;
            }

            if (affected <= 0) break;

            totalAffected += affected;
            batch++;
        }

        if (batch >= MaxBatches)
        {
            _logger.LogWarning(
                "SQLite backfill for {Table} stopped after {MaxBatches} batches ({TotalAffected} rows)",
                table, MaxBatches, totalAffected);
        }
        else
        {
            _logger.LogInformation(
                "SQLite backfill completed for {Table}: {TotalAffected} rows",
                table, totalAffected);
        }
    }

    private static async Task<int> ExecuteAsync(SqliteConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = sql;
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<int> ExecuteAsync(SqliteConnection conn, SqliteTransaction transaction, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandTimeout = 30;
        cmd.CommandText = sql;
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<long> ExecuteScalarAsync(SqliteConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is DBNull or null ? 0 : Convert.ToInt64(result);
    }

    private static async Task<string?> ExecuteScalarStringAsync(SqliteConnection conn, SqliteTransaction transaction, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandTimeout = 30;
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is DBNull or null ? null : result.ToString();
    }

    private static async Task<long> ExecuteScalarAsync(SqliteConnection conn, SqliteTransaction transaction, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandTimeout = 30;
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is DBNull or null ? 0 : Convert.ToInt64(result);
    }

}

