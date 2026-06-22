using Aneiang.Yarp.Storage;
using Microsoft.Data.Sqlite;

namespace Aneiang.Yarp.Storage.Sqlite;

/// <summary>SQLite implementation of <see cref="IProxyLogRepository"/>.</summary>
public sealed class SqliteProxyLogRepository : IProxyLogRepository
{
    private readonly SqliteConnectionFactory _connections;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public SqliteProxyLogRepository(SqliteConnectionFactory connections) => _connections = connections;

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
        await SqliteRepositoryInitializer.EnsureTableExistsAsync(_connections, "proxy_logs", ct);
        _initialized = true;
    }

    public async Task SaveProxyLogAsync(ProxyLogEntity log, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO proxy_logs (id, method, path, route_id, route_uid, route_key_snapshot, cluster_id, cluster_uid, cluster_key_snapshot, destination_id, destination_uid, destination_key_snapshot,
                status_code, duration_ms, request_body_size, response_body_size, client_ip,
                error_message, timestamp)
            VALUES (@id, @method, @path, @rid, @ruid, @rkey, @cid, @cuid, @ckey, @did, @duid, @dkey, @status, @dur, @reqsz, @respsz,
                @ip, @err, @ts)
            """;
        cmd.Parameters.AddWithValue("@id", log.Id);
        cmd.Parameters.AddWithValue("@method", log.Method ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@path", log.Path ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@rid", log.RouteId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ruid", log.RouteUid ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@rkey", log.RouteKeySnapshot ?? log.RouteId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@cid", log.ClusterId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@cuid", log.ClusterUid ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ckey", log.ClusterKeySnapshot ?? log.ClusterId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@did", log.DestinationId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@duid", log.DestinationUid ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@dkey", log.DestinationKeySnapshot ?? log.DestinationId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@status", log.StatusCode);
        cmd.Parameters.AddWithValue("@dur", log.DurationMs);
        cmd.Parameters.AddWithValue("@reqsz", log.RequestBodySize ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@respsz", log.ResponseBodySize ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ip", log.ClientIp ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@err", log.ErrorMessage ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ts", log.Timestamp.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ProxyLogEntity>> GetRecentProxyLogsAsync(int limit = 200, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM proxy_logs ORDER BY timestamp DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);
        var list = new List<ProxyLogEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(Map(reader));
        return list.AsReadOnly();
    }

    public async Task DeleteOldProxyLogsAsync(int keepCount, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM proxy_logs
            WHERE id NOT IN (SELECT id FROM proxy_logs ORDER BY timestamp DESC LIMIT @keep)
            """;
        cmd.Parameters.AddWithValue("@keep", keepCount);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static ProxyLogEntity Map(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        Method = r.IsDBNull(1) ? null : r.GetString(1),
        Path = r.IsDBNull(2) ? null : r.GetString(2),
        RouteId = r.IsDBNull(3) ? null : r.GetString(3),
        ClusterId = r.IsDBNull(4) ? null : r.GetString(4),
        DestinationId = r.IsDBNull(5) ? null : r.GetString(5),
        StatusCode = r.GetInt32(6),
        DurationMs = r.GetInt64(7),
        RequestBodySize = r.IsDBNull(8) ? null : r.GetInt64(8),
        ResponseBodySize = r.IsDBNull(9) ? null : r.GetInt64(9),
        ClientIp = r.IsDBNull(10) ? null : r.GetString(10),
        ErrorMessage = r.IsDBNull(11) ? null : r.GetString(11),
        Timestamp = DateTime.Parse(r.GetString(12))
    };
}
