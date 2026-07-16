using Aneiang.Yarp.Storage;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace Aneiang.Yarp.Storage.Sqlite;

public sealed class SqliteClusterRepository : IClusterRepository
{
    private readonly SqliteConnectionFactory _connections;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public SqliteClusterRepository(SqliteConnectionFactory connections) => _connections = connections;

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
        await SqliteRepositoryInitializer.EnsureTableExistsAsync(_connections, "yarp_clusters", ct);
        await SqliteRepositoryInitializer.EnsureTableExistsAsync(_connections, "yarp_destinations", ct);
        _initialized = true;
    }

    public async Task<ClusterEntity?> GetClusterAsync(string clusterId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM yarp_clusters WHERE cluster_id = @id";
        cmd.Parameters.AddWithValue("@id", clusterId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapCluster(reader) : null;
    }

    public async Task<IReadOnlyList<ClusterEntity>> GetAllClustersAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM yarp_clusters ORDER BY cluster_id";
        var list = new List<ClusterEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(MapCluster(reader));
        return list.AsReadOnly();
    }

    public async Task SaveClusterAsync(ClusterEntity cluster, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        cluster.UpdatedAt = DateTime.Now;
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await SaveOneAsync(conn, null, cluster, ct);
    }

    public async Task SaveClustersAsync(IEnumerable<ClusterEntity> clusters, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = conn.BeginTransaction();
        try
        {
            foreach (var c in clusters)
            {
                c.UpdatedAt = DateTime.Now;
                await SaveOneAsync(conn, tx, c, ct);
            }
            await tx.CommitAsync(ct);
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    public async Task DeleteClusterAsync(string clusterId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM yarp_clusters WHERE cluster_id = @id";
        cmd.Parameters.AddWithValue("@id", clusterId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<DestinationEntity>> GetDestinationsAsync(string clusterId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM yarp_destinations WHERE cluster_id = @cid";
        cmd.Parameters.AddWithValue("@cid", clusterId);
        var list = new List<DestinationEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(MapDestination(reader));
        return list.AsReadOnly();
    }

    public async Task SaveDestinationsAsync(string clusterId, IEnumerable<DestinationEntity> destinations, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = conn.BeginTransaction();
        try
        {
            await using (var delCmd = conn.CreateCommand())
            {
                delCmd.Transaction = tx;
                delCmd.CommandText = "DELETE FROM yarp_destinations WHERE cluster_id = @cid";
                delCmd.Parameters.AddWithValue("@cid", clusterId);
                await delCmd.ExecuteNonQueryAsync(ct);
            }
            foreach (var d in destinations)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO yarp_destinations (destination_id, cluster_id, address, host, healthy, metadata)
                    VALUES (@id, @cid, @addr, @host, @health, @meta)
                    ON CONFLICT(cluster_id, destination_id) DO UPDATE SET
                        address = @addr, host = @host, healthy = @health, metadata = @meta
                    """;
                cmd.Parameters.AddWithValue("@id", d.DestinationId);
                cmd.Parameters.AddWithValue("@cid", clusterId);
                cmd.Parameters.AddWithValue("@addr", d.Address);
                cmd.Parameters.AddWithValue("@host", d.Host ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@health", d.Healthy ? 1 : 0);
                cmd.Parameters.AddWithValue("@meta", d.Metadata ?? (object)DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    public async Task DeleteDestinationsAsync(string clusterId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM yarp_destinations WHERE cluster_id = @cid";
        cmd.Parameters.AddWithValue("@cid", clusterId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task SaveOneAsync(SqliteConnection conn, SqliteTransaction? tx, ClusterEntity c, CancellationToken ct)
    {
        var uid = string.IsNullOrWhiteSpace(c.ClusterUid) ? Guid.NewGuid().ToString("N") : c.ClusterUid;

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO yarp_clusters (cluster_uid, cluster_id, load_balancing_policy, health_check_config, circuit_breaker_config, source, created_by, created_at, updated_at, last_heartbeat, config_json)
            VALUES (@uid, @id, @lb, @hc, @cbc, @src, @cb, @ca, @ua, @lh, @cfg)
            ON CONFLICT(cluster_id) DO UPDATE SET
                cluster_uid = @uid, load_balancing_policy = @lb, health_check_config = @hc, circuit_breaker_config = @cbc,
                source = @src, updated_at = @ua, last_heartbeat = @lh, config_json = @cfg
            """;
        cmd.Parameters.AddWithValue("@uid", uid);
        cmd.Parameters.AddWithValue("@id", c.ClusterId);
        cmd.Parameters.AddWithValue("@lb", c.LoadBalancingPolicy ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@hc", c.HealthCheckConfig ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@cbc", c.CircuitBreakerConfig ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@src", c.Source);
        cmd.Parameters.AddWithValue("@cb", c.CreatedBy ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ca", c.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@ua", c.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@lh", c.LastHeartbeat?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@cfg", c.ConfigJson ?? (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static ClusterEntity MapCluster(SqliteDataReader r) => new()
    {
        ClusterUid = ReadString(r, "cluster_uid") ?? Guid.NewGuid().ToString("N"),
        ClusterId = ReadString(r, "cluster_id") ?? string.Empty,
        LoadBalancingPolicy = ReadString(r, "load_balancing_policy"),
        HealthCheckConfig = ReadString(r, "health_check_config"),
        CircuitBreakerConfig = ReadString(r, "circuit_breaker_config"),
        Source = ReadString(r, "source") ?? "dynamic",
        CreatedBy = ReadString(r, "created_by"),
        CreatedAt = ReadDateTime(r, "created_at") ?? DateTime.MinValue,
        UpdatedAt = ReadDateTime(r, "updated_at") ?? DateTime.MinValue,
        LastHeartbeat = ReadDateTime(r, "last_heartbeat"),
        ConfigJson = ReadString(r, "config_json")
    };

    private static string? ReadString(SqliteDataReader r, string name)
    {
        var ordinal = r.GetOrdinal(name);
        return r.IsDBNull(ordinal) ? null : r.GetString(ordinal);
    }

    private static DateTime? ReadDateTime(SqliteDataReader r, string name)
    {
        var value = ReadString(r, name);
        return string.IsNullOrEmpty(value) ? null : DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static DestinationEntity MapDestination(SqliteDataReader r) => new()
    {
        DestinationId = r.GetString(0),
        ClusterId = r.GetString(1),
        Address = r.GetString(2),
        Host = r.IsDBNull(3) ? null : r.GetString(3),
        Healthy = r.IsDBNull(4) || r.GetInt32(4) == 1,
        Metadata = r.IsDBNull(5) ? null : r.GetString(5)
    };
}
