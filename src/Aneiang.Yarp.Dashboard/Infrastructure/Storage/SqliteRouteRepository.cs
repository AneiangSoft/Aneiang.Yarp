using Aneiang.Yarp.Storage;
using Microsoft.Data.Sqlite;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Storage;

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
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
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
            CREATE INDEX IF NOT EXISTS ix_routes_cluster ON yarp_routes(cluster_id);
            """;
        await cmd.ExecuteNonQueryAsync(ct);
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
        route.UpdatedAt = DateTime.UtcNow;
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO yarp_routes (route_id, cluster_id, match_path, "order", transforms, source, created_by, created_at, updated_at, metadata)
            VALUES (@id, @cid, @path, @order, @trans, @src, @cb, @ca, @ua, @meta)
            ON CONFLICT(route_id) DO UPDATE SET
                cluster_id = @cid, match_path = @path, "order" = @order, transforms = @trans,
                source = @src, updated_at = @ua, metadata = @meta
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
                r.UpdatedAt = DateTime.UtcNow;
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO yarp_routes (route_id, cluster_id, match_path, "order", transforms, source, created_by, created_at, updated_at, metadata)
                    VALUES (@id, @cid, @path, @order, @trans, @src, @cb, @ca, @ua, @meta)
                    ON CONFLICT(route_id) DO UPDATE SET
                        cluster_id = @cid, match_path = @path, "order" = @order, transforms = @trans,
                        source = @src, updated_at = @ua, metadata = @meta
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
        cmd.Parameters.AddWithValue("@id", r.RouteId);
        cmd.Parameters.AddWithValue("@cid", r.ClusterId);
        cmd.Parameters.AddWithValue("@path", r.MatchPath);
        cmd.Parameters.AddWithValue("@order", r.Order);
        cmd.Parameters.AddWithValue("@trans", r.Transforms ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@src", r.Source);
        cmd.Parameters.AddWithValue("@cb", r.CreatedBy ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ca", r.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@ua", r.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@meta", r.Metadata ?? (object)DBNull.Value);
    }

    private static RouteEntity Map(SqliteDataReader r) => new()
    {
        RouteId = r.GetString(0),
        ClusterId = r.GetString(1),
        MatchPath = r.GetString(2),
        Order = r.GetInt32(3),
        Transforms = r.IsDBNull(4) ? null : r.GetString(4),
        Source = r.IsDBNull(5) ? "dynamic" : r.GetString(5),
        CreatedBy = r.IsDBNull(6) ? null : r.GetString(6),
        CreatedAt = r.IsDBNull(7) ? DateTime.MinValue : DateTime.Parse(r.GetString(7)),
        UpdatedAt = r.IsDBNull(8) ? DateTime.MinValue : DateTime.Parse(r.GetString(8)),
        Metadata = r.IsDBNull(9) ? null : r.GetString(9)
    };
}
