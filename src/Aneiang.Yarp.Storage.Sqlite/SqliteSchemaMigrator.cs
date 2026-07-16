using Aneiang.Yarp.Storage.Sqlite.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Storage.Sqlite;

public sealed class SqliteSchemaMigrator : IHostedService
{
    private readonly SqliteConnectionFactory _connections;
    private readonly ILogger<SqliteSchemaMigrator> _logger;

    private static readonly ISchemaMigration[] AllMigrations =
    [
        new Migration001_CoreTables(),
        new Migration002_PolicyAndAuditTables(),
        new Migration003_LogAndNotificationTables(),
        new Migration004_ColumnAdditions(),
        new Migration005_Indexes(),
        new Migration006_DataBackfill(),
        new Migration007_AITables(),
        new Migration008_ToolCallId(),
        new Migration009_AISettingsTable(),
    ];

    private static readonly Dictionary<string, string> LegacyIdMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["20260618_001_enterprise_identity_and_history_schema"] = "001_core_tables",
        ["20260619_001_storage_schema_centralization"] = "002_policy_audit_tables",
        ["20260622_001_destination_composite_pk"] = "004_uid_snapshot_columns",
        ["20260625_001_full_yarp_config_json"] = "004_uid_snapshot_columns",
        ["20260707_001_proxy_log_cold_hot_separation"] = "003_log_notification_tables",
        ["20260707_002_proxy_logs_meta_add_event_type"] = "004_uid_snapshot_columns",
        ["20260707_003_proxy_logs_meta_ensure_all_columns"] = "004_uid_snapshot_columns",
    };

    public SqliteSchemaMigrator(SqliteConnectionFactory connections, ILogger<SqliteSchemaMigrator> logger)
    {
        _connections = connections;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
        => await RunMigrationAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task RunMigrationAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = _connections.CreateRawConnection();
        await conn.OpenAsync(cancellationToken);

        await ExecutePragmaAsync(conn, "PRAGMA busy_timeout=30000;", cancellationToken);
        await ExecutePragmaAsync(conn, "PRAGMA wal_autocheckpoint=1000;", cancellationToken);

        await EnsureMigrationTableAsync(conn, cancellationToken);
        await MarkLegacyMigrationsAsync(conn, cancellationToken);

        foreach (var migration in AllMigrations.OrderBy(m => m.Version))
        {
            if (await IsMigrationAppliedAsync(conn, migration.Id, cancellationToken))
                continue;

            using var backfillCts = migration is Migration006_DataBackfill
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : null;
            backfillCts?.CancelAfter(TimeSpan.FromSeconds(30));
            var ct = backfillCts?.Token ?? cancellationToken;

            _logger.LogInformation("SQLite migration running: {MigrationId} — {Description}",
                migration.Id, migration.Description);

            await using var transaction = (SqliteTransaction)await conn.BeginTransactionAsync(cancellationToken);
            try
            {
                await migration.UpAsync(conn, transaction, ct);
                await MarkMigrationAppliedAsync(conn, transaction, migration.Id, migration.Description, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                _logger.LogInformation("SQLite migration completed: {MigrationId}", migration.Id);
            }
            catch (OperationCanceledException) when (migration is Migration006_DataBackfill)
            {
                try { await transaction.CommitAsync(CancellationToken.None); } catch { }
                _logger.LogWarning("SQLite data backfill exceeded 30s time limit; remaining rows will be backfilled on next startup");
            }
            catch (Exception ex)
            {
                try { await transaction.RollbackAsync(CancellationToken.None); }
                catch (Exception rbEx) { _logger.LogWarning(rbEx, "Failed to roll back migration transaction"); }
                _logger.LogError(ex, "SQLite migration failed and was rolled back: {MigrationId}", migration.Id);
                throw;
            }
        }
    }

    // ───────────────────────── Migration tracking ─────────────────────────

    private static async Task EnsureMigrationTableAsync(SqliteConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                id TEXT PRIMARY KEY,
                description TEXT NOT NULL,
                checksum TEXT,
                applied_at TEXT NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> IsMigrationAppliedAsync(SqliteConnection conn, string migrationId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", migrationId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    private static async Task MarkMigrationAppliedAsync(
        SqliteConnection conn, SqliteTransaction transaction,
        string migrationId, string description, CancellationToken ct)
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
        cmd.Parameters.AddWithValue("@at", DateTime.Now.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task MarkLegacyMigrationsAsync(SqliteConnection conn, CancellationToken ct)
    {
        await using var checkCmd = conn.CreateCommand();
        checkCmd.CommandTimeout = 30;
        checkCmd.CommandText = "SELECT id FROM schema_migrations";
        var existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var reader = await checkCmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                existingIds.Add(reader.GetString(0));
        }

        foreach (var (legacyId, newId) in LegacyIdMap)
        {
            if (existingIds.Contains(legacyId) && !existingIds.Contains(newId))
            {
                await using var insCmd = conn.CreateCommand();
                insCmd.CommandTimeout = 30;
                insCmd.CommandText = """
                    INSERT INTO schema_migrations (id, description, checksum, applied_at)
                    VALUES (@id, @desc, NULL, @at)
                    ON CONFLICT(id) DO NOTHING
                    """;
                insCmd.Parameters.AddWithValue("@id", newId);
                insCmd.Parameters.AddWithValue("@desc", $"Auto-mapped from legacy: {legacyId}");
                insCmd.Parameters.AddWithValue("@at", DateTime.Now.ToString("O"));
                await insCmd.ExecuteNonQueryAsync(ct);
            }
        }
    }

    private static async Task ExecutePragmaAsync(SqliteConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
