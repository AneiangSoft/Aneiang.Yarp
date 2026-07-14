using System.Text.Json;
using Aneiang.Yarp.Storage;
using Microsoft.Data.Sqlite;
using static Aneiang.Yarp.Storage.Sqlite.Migrations.MigrationHelper;

namespace Aneiang.Yarp.Storage.Sqlite.Migrations;

/// <summary>Backfill missing UIDs and snapshot columns (best-effort, time-boxed).</summary>
internal sealed class Migration006_DataBackfill : ISchemaMigration
{
    public int Version => 6;
    public string Id => "006_data_backfill";
    public string Description => "Backfill missing UIDs and snapshot columns";

    public async Task UpAsync(SqliteConnection conn, SqliteTransaction transaction, CancellationToken ct)
    {
        await BackfillAsync(conn, "yarp_clusters",
            "cluster_uid = lower(hex(randomblob(16)))",
            "cluster_uid IS NULL OR cluster_uid = ''", ct);

        await BackfillAsync(conn, "yarp_routes",
            "route_uid = lower(hex(randomblob(16)))",
            "route_uid IS NULL OR route_uid = ''", ct);

        await BackfillAsync(conn, "yarp_routes",
            "cluster_uid = (SELECT cluster_uid FROM yarp_clusters WHERE yarp_clusters.cluster_id = yarp_routes.cluster_id)",
            "(cluster_uid IS NULL OR cluster_uid = '') AND cluster_id IS NOT NULL", ct);

        await BackfillAsync(conn, "gateway_policies",
            "policy_uid = lower(hex(randomblob(16)))",
            "policy_uid IS NULL OR policy_uid = ''", ct);

        await BackfillAsync(conn, "config_audit_logs",
            "target_key_snapshot = target",
            "(target_key_snapshot IS NULL OR target_key_snapshot = '') AND target IS NOT NULL AND target <> ''", ct);

        await BackfillAsync(conn, "proxy_logs",
            "route_key_snapshot = COALESCE(route_key_snapshot, route_id), cluster_key_snapshot = COALESCE(cluster_key_snapshot, cluster_id), destination_key_snapshot = COALESCE(destination_key_snapshot, destination_id)",
            "(route_key_snapshot IS NULL AND route_id IS NOT NULL) OR (cluster_key_snapshot IS NULL AND cluster_id IS NOT NULL) OR (destination_key_snapshot IS NULL AND destination_id IS NOT NULL)", ct);

        await BackfillAsync(conn, "notification_history",
            "cluster_key_snapshot = COALESCE(cluster_key_snapshot, cluster_id), route_key_snapshot = COALESCE(route_key_snapshot, route_id)",
            "(cluster_key_snapshot IS NULL AND cluster_id IS NOT NULL) OR (route_key_snapshot IS NULL AND route_id IS NOT NULL)", ct);

        // Notification settings seed
        await ExecuteAsync(conn, transaction, """
            INSERT INTO notification_settings (id, enabled, updated_at)
            VALUES ('notification_settings', 1, datetime('now'))
            ON CONFLICT(id) DO NOTHING
            """, ct);

        // Migrate applied_targets JSON → policy_targets rows
        await MigrateAppliedTargetsAsync(conn, transaction, ct);
    }

    private static async Task BackfillAsync(SqliteConnection conn, string table, string setClause, string whereClause, CancellationToken ct)
    {
        await using var countCmd = conn.CreateCommand();
        countCmd.CommandTimeout = 30;
        countCmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {whereClause}";
        var rowsToFix = Convert.ToInt64(await countCmd.ExecuteScalarAsync(ct));
        if (rowsToFix <= 0) return;

        const int BatchSize = 1000;
        while (!ct.IsCancellationRequested)
        {
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
            int affected;
            try
            {
                affected = await ExecuteAsync(conn, tx, $"""
                    UPDATE {table} SET {setClause}
                    WHERE rowid IN (SELECT rowid FROM {table} WHERE {whereClause} LIMIT {BatchSize})
                    """, ct);
                await tx.CommitAsync(ct);
            }
            catch { try { await tx.RollbackAsync(CancellationToken.None); } catch { } throw; }
            if (affected <= 0) break;
        }
    }

    private static async Task MigrateAppliedTargetsAsync(SqliteConnection conn, SqliteTransaction transaction, CancellationToken ct)
    {
        await using var countCmd = conn.CreateCommand();
        countCmd.Transaction = transaction;
        countCmd.CommandText = "SELECT COUNT(*) FROM policy_targets";
        if (Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct)) > 0) return;

        var policies = new List<(string Uid, string Id, string Type, string? Applied, DateTime Updated)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = """
                SELECT policy_uid, policy_id, policy_type, applied_targets, updated_at
                FROM gateway_policies WHERE applied_targets IS NOT NULL AND applied_targets <> ''
                """;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                policies.Add((
                    reader.IsDBNull(0) ? Guid.NewGuid().ToString("N") : reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? "route" : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    DateTime.TryParse(reader.IsDBNull(4) ? null : reader.GetString(4), out var u) ? u : DateTime.Now));
            }
        }

        foreach (var p in policies)
        {
            List<string>? targets;
            try { targets = JsonSerializer.Deserialize<List<string>>(p.Applied ?? "[]"); } catch { continue; }
            if (targets == null) continue;

            foreach (var key in targets.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await using var ins = conn.CreateCommand();
                ins.Transaction = transaction;
                ins.CommandText = """
                    INSERT OR IGNORE INTO policy_targets (id, policy_uid, policy_id, target_type, target_uid, target_key_snapshot, created_at)
                    VALUES (@id, @puid, @pid, @type, @tuid, @tkey, @ca)
                    """;
                ins.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                ins.Parameters.AddWithValue("@puid", p.Uid);
                ins.Parameters.AddWithValue("@pid", p.Id);
                ins.Parameters.AddWithValue("@type", p.Type);
                ins.Parameters.AddWithValue("@tuid", StableUid.FromKey(p.Type, key));
                ins.Parameters.AddWithValue("@tkey", key);
                ins.Parameters.AddWithValue("@ca", p.Updated.ToString("O"));
                await ins.ExecuteNonQueryAsync(ct);
            }
        }
    }
}
