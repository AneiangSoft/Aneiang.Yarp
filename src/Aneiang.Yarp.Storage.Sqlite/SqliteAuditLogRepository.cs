using Aneiang.Yarp.Storage;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace Aneiang.Yarp.Storage.Sqlite;

/// <summary>SQLite implementation of <see cref="IAuditLogRepository"/>.</summary>
public sealed class SqliteAuditLogRepository : IAuditLogRepository
{
    private readonly SqliteConnectionFactory _connections;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public SqliteAuditLogRepository(SqliteConnectionFactory connections) => _connections = connections;

    private async ValueTask EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;
        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            await InitializeAsync(ct);
        }
        finally { _initLock.Release(); }
    }

    private async Task InitializeAsync(CancellationToken ct)
    {
        await SqliteRepositoryInitializer.EnsureTableExistsAsync(_connections, "config_audit_logs", ct);
        _initialized = true;
    }

    public async Task<IReadOnlyList<AuditLogEntity>> GetAuditLogsAsync(int limit = 200, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM config_audit_logs ORDER BY timestamp DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);
        var list = new List<AuditLogEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(Map(reader));
        return list.AsReadOnly();
    }

    public async Task<IReadOnlyList<AuditLogEntity>> GetAuditLogsByTargetAsync(string target, int limit = 50, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM config_audit_logs WHERE target = @target ORDER BY timestamp DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@target", target);
        cmd.Parameters.AddWithValue("@limit", limit);
        var list = new List<AuditLogEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(Map(reader));
        return list.AsReadOnly();
    }

    public async Task SaveAuditLogAsync(AuditLogEntity audit, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO config_audit_logs (id, action, target, target_type, target_uid, target_key_snapshot, target_display_name_snapshot, operator, client_ip, before_data, after_data, success, error_message, timestamp)
            VALUES (@id, @act, @tgt, @ttype, @tuid, @tkey, @tdn, @op, @ip, @before, @after, @succ, @err, @ts)
            """;
        cmd.Parameters.AddWithValue("@id", audit.Id);
        cmd.Parameters.AddWithValue("@act", audit.Action);
        cmd.Parameters.AddWithValue("@tgt", audit.Target);
        cmd.Parameters.AddWithValue("@ttype", audit.TargetType ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@tuid", audit.TargetUid ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@tkey", audit.TargetKeySnapshot ?? audit.Target);
        cmd.Parameters.AddWithValue("@tdn", audit.TargetDisplayNameSnapshot ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@op", audit.Operator ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ip", audit.ClientIp ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@before", audit.BeforeData ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@after", audit.AfterData ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@succ", audit.Success ? 1 : 0);
        cmd.Parameters.AddWithValue("@err", audit.ErrorMessage ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ts", audit.Timestamp.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteOldAuditLogsAsync(int keepCount, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM config_audit_logs
            WHERE id NOT IN (SELECT id FROM config_audit_logs ORDER BY timestamp DESC LIMIT @keep)
            """;
        cmd.Parameters.AddWithValue("@keep", keepCount);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static AuditLogEntity Map(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        Action = r.GetString(1),
        Target = r.GetString(2),
        TargetType = r.IsDBNull(3) ? null : r.GetString(3),
        Operator = r.IsDBNull(4) ? null : r.GetString(4),
        ClientIp = r.IsDBNull(5) ? null : r.GetString(5),
        BeforeData = r.IsDBNull(6) ? null : r.GetString(6),
        AfterData = r.IsDBNull(7) ? null : r.GetString(7),
        Success = r.GetInt32(8) == 1,
        ErrorMessage = r.IsDBNull(9) ? null : r.GetString(9),
        Timestamp = DateTime.Parse(r.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
    };
}
