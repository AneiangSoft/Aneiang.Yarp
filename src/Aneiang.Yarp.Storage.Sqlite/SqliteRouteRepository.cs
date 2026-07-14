using Aneiang.Yarp.Storage;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace Aneiang.Yarp.Storage.Sqlite;

/// <summary>SQLite implementation of <see cref="IRouteRepository"/>.</summary>
public sealed class SqliteRouteRepository : IRouteRepository
{
    private readonly SqliteConnectionFactory _connections;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public SqliteRouteRepository(SqliteConnectionFactory connections) => _connections = connections;

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
        await SqliteRepositoryInitializer.EnsureTableExistsAsync(_connections, "yarp_routes", ct);
        _initialized = true;
    }

    public async Task<RouteEntity?> GetRouteAsync(string routeId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM yarp_routes WHERE route_id = @id";
        cmd.Parameters.AddWithValue("@id", routeId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<RouteEntity>> GetAllRoutesAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """SELECT * FROM yarp_routes ORDER BY "order", route_id""";
        var list = new List<RouteEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(Map(reader));
        return list.AsReadOnly();
    }

    public async Task<IReadOnlyList<RouteEntity>> GetRoutesByClusterAsync(string clusterId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """SELECT * FROM yarp_routes WHERE cluster_id = @cid ORDER BY "order" """;
        cmd.Parameters.AddWithValue("@cid", clusterId);
        var list = new List<RouteEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(Map(reader));
        return list.AsReadOnly();
    }

    public async Task SaveRouteAsync(RouteEntity route, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        route.UpdatedAt = DateTime.Now;
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO yarp_routes (route_uid, route_id, cluster_uid, cluster_id, match_path, "order", transforms, source, created_by, created_at, updated_at, metadata, config_json)
            VALUES (@uid, @id, @cuid, @cid, @path, @order, @trans, @src, @cb, @ca, @ua, @meta, @cfg)
            ON CONFLICT(route_id) DO UPDATE SET
                route_uid = @uid, cluster_uid = @cuid, cluster_id = @cid, match_path = @path, "order" = @order, transforms = @trans,
                source = @src, updated_at = @ua, metadata = @meta, config_json = @cfg
            """;
        AddParams(cmd, route);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveRoutesAsync(IEnumerable<RouteEntity> routes, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = conn.BeginTransaction();
        try
        {
            foreach (var r in routes)
            {
                r.UpdatedAt = DateTime.Now;
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO yarp_routes (route_uid, route_id, cluster_uid, cluster_id, match_path, "order", transforms, source, created_by, created_at, updated_at, metadata, config_json)
                    VALUES (@uid, @id, @cuid, @cid, @path, @order, @trans, @src, @cb, @ca, @ua, @meta, @cfg)
                    ON CONFLICT(route_id) DO UPDATE SET
                        route_uid = @uid, cluster_uid = @cuid, cluster_id = @cid, match_path = @path, "order" = @order, transforms = @trans,
                        source = @src, updated_at = @ua, metadata = @meta, config_json = @cfg
                    """;
                AddParams(cmd, r);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    public async Task DeleteRouteAsync(string routeId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM yarp_routes WHERE route_id = @id";
        cmd.Parameters.AddWithValue("@id", routeId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteRoutesByClusterAsync(string clusterId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM yarp_routes WHERE cluster_id = @cid";
        cmd.Parameters.AddWithValue("@cid", clusterId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AddParams(SqliteCommand cmd, RouteEntity r)
    {
        cmd.Parameters.AddWithValue("@uid", string.IsNullOrWhiteSpace(r.RouteUid) ? Guid.NewGuid().ToString("N") : r.RouteUid);
        cmd.Parameters.AddWithValue("@id", r.RouteId);
        cmd.Parameters.AddWithValue("@cuid", r.ClusterUid ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@cid", r.ClusterId);
        cmd.Parameters.AddWithValue("@path", r.MatchPath);
        cmd.Parameters.AddWithValue("@order", r.Order);
        cmd.Parameters.AddWithValue("@trans", r.Transforms ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@src", r.Source);
        cmd.Parameters.AddWithValue("@cb", r.CreatedBy ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ca", r.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@ua", r.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@meta", r.Metadata ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@cfg", r.ConfigJson ?? (object)DBNull.Value);
    }

    private static RouteEntity Map(SqliteDataReader r) => new()
    {
        RouteUid = ReadString(r, "route_uid") ?? Guid.NewGuid().ToString("N"),
        RouteId = ReadString(r, "route_id") ?? string.Empty,
        ClusterUid = ReadString(r, "cluster_uid"),
        ClusterId = ReadString(r, "cluster_id") ?? string.Empty,
        MatchPath = ReadString(r, "match_path") ?? string.Empty,
        Order = r.GetInt32(r.GetOrdinal("order")),
        Transforms = ReadString(r, "transforms"),
        Source = ReadString(r, "source") ?? "dynamic",
        CreatedBy = ReadString(r, "created_by"),
        CreatedAt = ReadDateTime(r, "created_at") ?? DateTime.MinValue,
        UpdatedAt = ReadDateTime(r, "updated_at") ?? DateTime.MinValue,
        Metadata = ReadString(r, "metadata"),
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
}
