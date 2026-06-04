using System.Text.Json;
using Aneiang.Yarp.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Storage;

/// <summary>
/// SQLite-based structured data store implementation.
/// Each entity type has its own table with proper schema.
/// </summary>
public class StructuredSqliteStore : IStructuredDataStore
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _connectionString;
    private readonly ILogger<StructuredSqliteStore> _logger;
    private bool _initialized;
    private static bool _providerSet;

    public StructuredSqliteStore(StorageOptions options, ILogger<StructuredSqliteStore> logger)
    {
        _connectionString = options.Sqlite.ConnectionString;
        _logger = logger;
        EnsureProvider();
    }

    private static void EnsureProvider()
    {
        if (_providerSet) return;
        SQLitePCL.Batteries_V2.Init();
        _providerSet = true;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            -- Routes table
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
                enabled INTEGER DEFAULT 1
            );
            CREATE INDEX IF NOT EXISTS ix_routes_cluster ON yarp_routes(cluster_id);
            CREATE INDEX IF NOT EXISTS ix_routes_enabled ON yarp_routes(enabled);

            -- Clusters table
            CREATE TABLE IF NOT EXISTS yarp_clusters (
                cluster_id TEXT PRIMARY KEY,
                load_balancing_policy TEXT,
                health_check_config TEXT,
                source TEXT DEFAULT 'dynamic',
                created_by TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                last_heartbeat TEXT,
                enabled INTEGER DEFAULT 1
            );
            CREATE INDEX IF NOT EXISTS ix_clusters_enabled ON yarp_clusters(enabled);

            -- Destinations table
            CREATE TABLE IF NOT EXISTS yarp_destinations (
                destination_id TEXT PRIMARY KEY,
                cluster_id TEXT NOT NULL,
                address TEXT NOT NULL,
                host TEXT,
                healthy INTEGER DEFAULT 1,
                metadata TEXT,
                FOREIGN KEY (cluster_id) REFERENCES yarp_clusters(cluster_id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_destinations_cluster ON yarp_destinations(cluster_id);

            -- Config history table
            CREATE TABLE IF NOT EXISTS yarp_config_history (
                version_id TEXT PRIMARY KEY,
                description TEXT,
                client_ip TEXT,
                config_data TEXT NOT NULL,
                diff_data TEXT,
                created_by TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_config_history_created ON yarp_config_history(created_at DESC);

            -- Gateway policies table
            CREATE TABLE IF NOT EXISTS gateway_policies (
                policy_id TEXT PRIMARY KEY,
                display_name TEXT NOT NULL,
                description TEXT,
                priority INTEGER DEFAULT 50,
                enabled INTEGER DEFAULT 1,
                circuit_breaker_config TEXT,
                retry_config TEXT,
                rate_limit_config TEXT,
                waf_config TEXT,
                custom_plugins TEXT,
                tags TEXT,
                created_by TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_policies_enabled ON gateway_policies(enabled);

            -- Config audit logs table
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
            CREATE INDEX IF NOT EXISTS ix_audit_timestamp ON config_audit_logs(timestamp DESC);
            CREATE INDEX IF NOT EXISTS ix_audit_target ON config_audit_logs(target);

            -- Webhook settings table (single row)
            CREATE TABLE IF NOT EXISTS webhook_settings (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                enabled INTEGER DEFAULT 0,
                endpoints TEXT,
                events TEXT,
                timeout_seconds INTEGER DEFAULT 30,
                retry_count INTEGER DEFAULT 3,
                secret TEXT,
                updated_at TEXT NOT NULL
            );
            """;

        await cmd.ExecuteNonQueryAsync(ct);
        _initialized = true;
        _logger.LogInformation("StructuredSqliteStore initialized");
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (!_initialized) await InitializeAsync(ct);
    }

    private SqliteConnection CreateConnection() => new(_connectionString);

    // ========== Routes ==========

    public async Task<RouteEntity?> GetRouteAsync(string routeId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM yarp_routes WHERE route_id = @id";
        cmd.Parameters.AddWithValue("@id", routeId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return MapRoute(reader);
    }

    public async Task<IReadOnlyList<RouteEntity>> GetAllRoutesAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM yarp_routes ORDER BY \"order\", route_id";

        var routes = new List<RouteEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            routes.Add(MapRoute(reader));
        return routes.AsReadOnly();
    }

    public async Task<IReadOnlyList<RouteEntity>> GetRoutesByClusterAsync(string clusterId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM yarp_routes WHERE cluster_id = @cid ORDER BY \"order\"";
        cmd.Parameters.AddWithValue("@cid", clusterId);

        var routes = new List<RouteEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            routes.Add(MapRoute(reader));
        return routes.AsReadOnly();
    }

    public async Task SaveRouteAsync(RouteEntity route, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        route.UpdatedAt = DateTime.UtcNow;

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO yarp_routes (route_id, cluster_id, match_path, "order", transforms, source, created_by, created_at, updated_at, metadata, enabled)
            VALUES (@id, @cid, @path, @order, @trans, @src, @cb, @ca, @ua, @meta, @en)
            ON CONFLICT(route_id) DO UPDATE SET
                cluster_id = @cid, match_path = @path, "order" = @order, transforms = @trans,
                source = @src, updated_at = @ua, metadata = @meta, enabled = @en
            """;
        AddRouteParams(cmd, route);
        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogDebug("Route {RouteId} saved", route.RouteId);
    }

    public async Task SaveRoutesAsync(IEnumerable<RouteEntity> routes, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = conn.BeginTransaction();

        try
        {
            foreach (var route in routes)
            {
                route.UpdatedAt = DateTime.UtcNow;
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO yarp_routes (route_id, cluster_id, match_path, "order", transforms, source, created_by, created_at, updated_at, metadata, enabled)
                    VALUES (@id, @cid, @path, @order, @trans, @src, @cb, @ca, @ua, @meta, @en)
                    ON CONFLICT(route_id) DO UPDATE SET
                        cluster_id = @cid, match_path = @path, "order" = @order, transforms = @trans,
                        source = @src, updated_at = @ua, metadata = @meta, enabled = @en
                    """;
                AddRouteParams(cmd, route);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            _logger.LogDebug("Batch saved {Count} routes", routes.Count());
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static void AddRouteParams(SqliteCommand cmd, RouteEntity route)
    {
        cmd.Parameters.AddWithValue("@id", route.RouteId);
        cmd.Parameters.AddWithValue("@cid", route.ClusterId);
        cmd.Parameters.AddWithValue("@path", route.MatchPath);
        cmd.Parameters.AddWithValue("@order", route.Order);
        cmd.Parameters.AddWithValue("@trans", route.Transforms ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@src", route.Source);
        cmd.Parameters.AddWithValue("@cb", route.CreatedBy ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ca", route.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@ua", route.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@meta", route.Metadata ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@en", route.Enabled ? 1 : 0);
    }

    public async Task DeleteRouteAsync(string routeId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM yarp_routes WHERE route_id = @id";
        cmd.Parameters.AddWithValue("@id", routeId);
        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogDebug("Route {RouteId} deleted", routeId);
    }

    public async Task DeleteRoutesByClusterAsync(string clusterId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM yarp_routes WHERE cluster_id = @cid";
        cmd.Parameters.AddWithValue("@cid", clusterId);
        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogDebug("Routes for cluster {ClusterId} deleted", clusterId);
    }

    private static RouteEntity MapRoute(SqliteDataReader reader) => new()
    {
        RouteId = reader.GetString(0),
        ClusterId = reader.GetString(1),
        MatchPath = reader.GetString(2),
        Order = reader.GetInt32(3),
        Transforms = reader.IsDBNull(4) ? null : reader.GetString(4),
        Source = reader.IsDBNull(5) ? "dynamic" : reader.GetString(5),
        CreatedBy = reader.IsDBNull(6) ? null : reader.GetString(6),
        CreatedAt = reader.IsDBNull(7) ? DateTime.MinValue : DateTime.Parse(reader.GetString(7)),
        UpdatedAt = reader.IsDBNull(8) ? DateTime.MinValue : DateTime.Parse(reader.GetString(8)),
        Metadata = reader.IsDBNull(9) ? null : reader.GetString(9),
        Enabled = reader.IsDBNull(10) || reader.GetInt32(10) == 1
    };

    // ========== Clusters ==========

    public async Task<ClusterEntity?> GetClusterAsync(string clusterId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM yarp_clusters WHERE cluster_id = @id";
        cmd.Parameters.AddWithValue("@id", clusterId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return MapCluster(reader);
    }

    public async Task<IReadOnlyList<ClusterEntity>> GetAllClustersAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM yarp_clusters ORDER BY cluster_id";

        var clusters = new List<ClusterEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            clusters.Add(MapCluster(reader));
        return clusters.AsReadOnly();
    }

    public async Task SaveClusterAsync(ClusterEntity cluster, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        cluster.UpdatedAt = DateTime.UtcNow;

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await SaveClusterInternalAsync(conn, null, cluster, ct);
        _logger.LogDebug("Cluster {ClusterId} saved", cluster.ClusterId);
    }

    public async Task SaveClustersAsync(IEnumerable<ClusterEntity> clusters, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = conn.BeginTransaction();

        try
        {
            foreach (var cluster in clusters)
            {
                cluster.UpdatedAt = DateTime.UtcNow;
                await SaveClusterInternalAsync(conn, tx, cluster, ct);
            }
            await tx.CommitAsync(ct);
            _logger.LogDebug("Batch saved {Count} clusters", clusters.Count());
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static async Task SaveClusterInternalAsync(SqliteConnection conn, SqliteTransaction tx, ClusterEntity cluster, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;

        cmd.CommandText = """
            INSERT INTO yarp_clusters (cluster_id, load_balancing_policy, health_check_config, source, created_by, created_at, updated_at, last_heartbeat, enabled)
            VALUES (@id, @lb, @hc, @src, @cb, @ca, @ua, @lh, @en)
            ON CONFLICT(cluster_id) DO UPDATE SET
                load_balancing_policy = @lb, health_check_config = @hc, source = @src,
                updated_at = @ua, last_heartbeat = @lh, enabled = @en
            """;
        cmd.Parameters.AddWithValue("@id", cluster.ClusterId);
        cmd.Parameters.AddWithValue("@lb", cluster.LoadBalancingPolicy ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@hc", cluster.HealthCheckConfig ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@src", cluster.Source);
        cmd.Parameters.AddWithValue("@cb", cluster.CreatedBy ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ca", cluster.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@ua", cluster.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@lh", cluster.LastHeartbeat?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@en", cluster.Enabled ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteClusterAsync(string clusterId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM yarp_clusters WHERE cluster_id = @id";
        cmd.Parameters.AddWithValue("@id", clusterId);
        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogDebug("Cluster {ClusterId} deleted", clusterId);
    }

    private static ClusterEntity MapCluster(SqliteDataReader reader) => new()
    {
        ClusterId = reader.GetString(0),
        LoadBalancingPolicy = reader.IsDBNull(1) ? null : reader.GetString(1),
        HealthCheckConfig = reader.IsDBNull(2) ? null : reader.GetString(2),
        Source = reader.IsDBNull(3) ? "dynamic" : reader.GetString(3),
        CreatedBy = reader.IsDBNull(4) ? null : reader.GetString(4),
        CreatedAt = reader.IsDBNull(5) ? DateTime.MinValue : DateTime.Parse(reader.GetString(5)),
        UpdatedAt = reader.IsDBNull(6) ? DateTime.MinValue : DateTime.Parse(reader.GetString(6)),
        LastHeartbeat = reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7)),
        Enabled = reader.IsDBNull(8) || reader.GetInt32(8) == 1
    };

    // ========== Destinations ==========

    public async Task<IReadOnlyList<DestinationEntity>> GetDestinationsAsync(string clusterId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM yarp_destinations WHERE cluster_id = @cid";
        cmd.Parameters.AddWithValue("@cid", clusterId);

        var destinations = new List<DestinationEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            destinations.Add(MapDestination(reader));
        return destinations.AsReadOnly();
    }

    public async Task SaveDestinationsAsync(string clusterId, IEnumerable<DestinationEntity> destinations, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = conn.BeginTransaction();

        try
        {
            // Delete existing destinations for this cluster
            await using (var delCmd = conn.CreateCommand())
            {
                delCmd.Transaction = tx;
                delCmd.CommandText = "DELETE FROM yarp_destinations WHERE cluster_id = @cid";
                delCmd.Parameters.AddWithValue("@cid", clusterId);
                await delCmd.ExecuteNonQueryAsync(ct);
            }

            // Insert new destinations
            foreach (var dest in destinations)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO yarp_destinations (destination_id, cluster_id, address, host, healthy, metadata)
                    VALUES (@id, @cid, @addr, @host, @health, @meta)
                    ON CONFLICT(destination_id) DO UPDATE SET
                        address = @addr, host = @host, healthy = @health, metadata = @meta
                    """;
                cmd.Parameters.AddWithValue("@id", dest.DestinationId);
                cmd.Parameters.AddWithValue("@cid", clusterId);
                cmd.Parameters.AddWithValue("@addr", dest.Address);
                cmd.Parameters.AddWithValue("@host", dest.Host ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@health", dest.Healthy ? 1 : 0);
                cmd.Parameters.AddWithValue("@meta", dest.Metadata ?? (object)DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            _logger.LogDebug("Saved {Count} destinations for cluster {ClusterId}", destinations.Count(), clusterId);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task DeleteDestinationsAsync(string clusterId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM yarp_destinations WHERE cluster_id = @cid";
        cmd.Parameters.AddWithValue("@cid", clusterId);
        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogDebug("Destinations for cluster {ClusterId} deleted", clusterId);
    }

    private DestinationEntity MapDestination(SqliteDataReader reader) => new()
    {
        DestinationId = reader.GetString(0),
        ClusterId = reader.GetString(1),
        Address = reader.GetString(2),
        Host = reader.IsDBNull(3) ? null : reader.GetString(3),
        Healthy = reader.IsDBNull(4) || reader.GetInt32(4) == 1,
        Metadata = reader.IsDBNull(5) ? null : reader.GetString(5)
    };

    // ========== Config History ==========

    public async Task<ConfigHistoryEntity?> GetConfigHistoryAsync(string versionId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM yarp_config_history WHERE version_id = @id";
        cmd.Parameters.AddWithValue("@id", versionId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return MapConfigHistory(reader);
    }

    public async Task<IReadOnlyList<ConfigHistoryEntity>> GetConfigHistoryListAsync(int limit = 50, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM yarp_config_history ORDER BY created_at DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);

        var list = new List<ConfigHistoryEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapConfigHistory(reader));
        return list.AsReadOnly();
    }

    public async Task SaveConfigHistoryAsync(ConfigHistoryEntity history, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO yarp_config_history (version_id, description, client_ip, config_data, diff_data, created_by, created_at)
            VALUES (@id, @desc, @ip, @cfg, @diff, @cb, @ca)
            ON CONFLICT(version_id) DO UPDATE SET
                description = @desc, client_ip = @ip, config_data = @cfg, diff_data = @diff
            """;
        cmd.Parameters.AddWithValue("@id", history.VersionId);
        cmd.Parameters.AddWithValue("@desc", history.Description ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ip", history.ClientIp ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@cfg", history.ConfigData);
        cmd.Parameters.AddWithValue("@diff", history.DiffData ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@cb", history.CreatedBy);
        cmd.Parameters.AddWithValue("@ca", history.CreatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogDebug("Config history {VersionId} saved", history.VersionId);
    }

    public async Task DeleteConfigHistoryAsync(string versionId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM yarp_config_history WHERE version_id = @id";
        cmd.Parameters.AddWithValue("@id", versionId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteOldConfigHistoryAsync(int keepCount, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM yarp_config_history
            WHERE version_id NOT IN (
                SELECT version_id FROM yarp_config_history ORDER BY created_at DESC LIMIT @keep
            )
            """;
        cmd.Parameters.AddWithValue("@keep", keepCount);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private ConfigHistoryEntity MapConfigHistory(SqliteDataReader reader) => new()
    {
        VersionId = reader.GetString(0),
        Description = reader.IsDBNull(1) ? null : reader.GetString(1),
        ClientIp = reader.IsDBNull(2) ? null : reader.GetString(2),
        ConfigData = reader.GetString(3),
        DiffData = reader.IsDBNull(4) ? null : reader.GetString(4),
        CreatedBy = reader.GetString(5),
        CreatedAt = DateTime.Parse(reader.GetString(6))
    };

    // ========== Policies ==========

    public async Task<PolicyEntity?> GetPolicyAsync(string policyId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM gateway_policies WHERE policy_id = @id";
        cmd.Parameters.AddWithValue("@id", policyId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return MapPolicy(reader);
    }

    public async Task<IReadOnlyList<PolicyEntity>> GetAllPoliciesAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM gateway_policies ORDER BY priority DESC, display_name";

        var policies = new List<PolicyEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            policies.Add(MapPolicy(reader));
        return policies.AsReadOnly();
    }

    public async Task SavePolicyAsync(PolicyEntity policy, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        policy.UpdatedAt = DateTime.UtcNow;

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO gateway_policies (policy_id, display_name, description, priority, enabled, circuit_breaker_config, retry_config, rate_limit_config, waf_config, custom_plugins, tags, created_by, created_at, updated_at)
            VALUES (@id, @name, @desc, @pri, @en, @cb, @retry, @rl, @waf, @plugins, @tags, @cby, @ca, @ua)
            ON CONFLICT(policy_id) DO UPDATE SET
                display_name = @name, description = @desc, priority = @pri, enabled = @en,
                circuit_breaker_config = @cb, retry_config = @retry, rate_limit_config = @rl,
                waf_config = @waf, custom_plugins = @plugins, tags = @tags, updated_at = @ua
            """;
        cmd.Parameters.AddWithValue("@id", policy.PolicyId);
        cmd.Parameters.AddWithValue("@name", policy.DisplayName);
        cmd.Parameters.AddWithValue("@desc", policy.Description ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@pri", policy.Priority);
        cmd.Parameters.AddWithValue("@en", policy.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@cb", policy.CircuitBreakerConfig ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@retry", policy.RetryConfig ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@rl", policy.RateLimitConfig ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@waf", policy.WafConfig ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@plugins", policy.CustomPlugins ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@tags", policy.Tags ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@cby", policy.CreatedBy ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ca", policy.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@ua", policy.UpdatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogDebug("Policy {PolicyId} saved", policy.PolicyId);
    }

    public async Task DeletePolicyAsync(string policyId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM gateway_policies WHERE policy_id = @id";
        cmd.Parameters.AddWithValue("@id", policyId);
        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogDebug("Policy {PolicyId} deleted", policyId);
    }

    private static PolicyEntity MapPolicy(SqliteDataReader reader) => new()
    {
        PolicyId = reader.GetString(0),
        DisplayName = reader.GetString(1),
        Description = reader.IsDBNull(2) ? null : reader.GetString(2),
        Priority = reader.GetInt32(3),
        Enabled = reader.GetInt32(4) == 1,
        CircuitBreakerConfig = reader.IsDBNull(5) ? null : reader.GetString(5),
        RetryConfig = reader.IsDBNull(6) ? null : reader.GetString(6),
        RateLimitConfig = reader.IsDBNull(7) ? null : reader.GetString(7),
        WafConfig = reader.IsDBNull(8) ? null : reader.GetString(8),
        CustomPlugins = reader.IsDBNull(9) ? null : reader.GetString(9),
        Tags = reader.IsDBNull(10) ? null : reader.GetString(10),
        CreatedBy = reader.IsDBNull(11) ? null : reader.GetString(11),
        CreatedAt = reader.IsDBNull(12) ? DateTime.MinValue : DateTime.Parse(reader.GetString(12)),
        UpdatedAt = reader.IsDBNull(13) ? DateTime.MinValue : DateTime.Parse(reader.GetString(13))
    };

    // ========== Audit Logs ==========

    public async Task<IReadOnlyList<AuditLogEntity>> GetAuditLogsAsync(int limit = 200, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM config_audit_logs ORDER BY timestamp DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);

        var logs = new List<AuditLogEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            logs.Add(MapAuditLog(reader));
        return logs.AsReadOnly();
    }

    public async Task<IReadOnlyList<AuditLogEntity>> GetAuditLogsByTargetAsync(string target, int limit = 50, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM config_audit_logs WHERE target = @target ORDER BY timestamp DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@target", target);
        cmd.Parameters.AddWithValue("@limit", limit);

        var logs = new List<AuditLogEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            logs.Add(MapAuditLog(reader));
        return logs.AsReadOnly();
    }

    public async Task SaveAuditLogAsync(AuditLogEntity audit, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO config_audit_logs (id, action, target, target_type, operator, client_ip, before_data, after_data, success, error_message, timestamp)
            VALUES (@id, @act, @tgt, @ttype, @op, @ip, @before, @after, @succ, @err, @ts)
            """;
        cmd.Parameters.AddWithValue("@id", audit.Id);
        cmd.Parameters.AddWithValue("@act", audit.Action);
        cmd.Parameters.AddWithValue("@tgt", audit.Target);
        cmd.Parameters.AddWithValue("@ttype", audit.TargetType ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@op", audit.Operator ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ip", audit.ClientIp ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@before", audit.BeforeData ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@after", audit.AfterData ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@succ", audit.Success ? 1 : 0);
        cmd.Parameters.AddWithValue("@err", audit.ErrorMessage ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ts", audit.Timestamp.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogDebug("Audit log {Id} saved", audit.Id);
    }

    public async Task DeleteOldAuditLogsAsync(int keepCount, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM config_audit_logs
            WHERE id NOT IN (
                SELECT id FROM config_audit_logs ORDER BY timestamp DESC LIMIT @keep
            )
            """;
        cmd.Parameters.AddWithValue("@keep", keepCount);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static AuditLogEntity MapAuditLog(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Action = reader.GetString(1),
        Target = reader.GetString(2),
        TargetType = reader.IsDBNull(3) ? null : reader.GetString(3),
        Operator = reader.IsDBNull(4) ? null : reader.GetString(4),
        ClientIp = reader.IsDBNull(5) ? null : reader.GetString(5),
        BeforeData = reader.IsDBNull(6) ? null : reader.GetString(6),
        AfterData = reader.IsDBNull(7) ? null : reader.GetString(7),
        Success = reader.GetInt32(8) == 1,
        ErrorMessage = reader.IsDBNull(9) ? null : reader.GetString(9),
        Timestamp = DateTime.Parse(reader.GetString(10))
    };

    // ========== Webhook Settings ==========

    public async Task<WebhookSettingsEntity?> GetWebhookSettingsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM webhook_settings WHERE id = 1";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return new WebhookSettingsEntity
        {
            Enabled = reader.GetInt32(1) == 1,
            Endpoints = reader.IsDBNull(2) ? null : reader.GetString(2),
            Events = reader.IsDBNull(3) ? null : reader.GetString(3),
            TimeoutSeconds = reader.GetInt32(4),
            RetryCount = reader.GetInt32(5),
            Secret = reader.IsDBNull(6) ? null : reader.GetString(6),
            UpdatedAt = DateTime.Parse(reader.GetString(7))
        };
    }

    public async Task SaveWebhookSettingsAsync(WebhookSettingsEntity settings, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO webhook_settings (id, enabled, endpoints, events, timeout_seconds, retry_count, secret, updated_at)
            VALUES (1, @en, @ep, @ev, @to, @retry, @secret, @ua)
            ON CONFLICT(id) DO UPDATE SET
                enabled = @en, endpoints = @ep, events = @ev, timeout_seconds = @to, retry_count = @retry, secret = @secret, updated_at = @ua
            """;
        cmd.Parameters.AddWithValue("@en", settings.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@ep", settings.Endpoints ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ev", settings.Events ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@to", settings.TimeoutSeconds);
        cmd.Parameters.AddWithValue("@retry", settings.RetryCount);
        cmd.Parameters.AddWithValue("@secret", settings.Secret ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ua", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogDebug("Webhook settings saved");
    }

    public void Dispose()
    {
        // Connection is disposed per operation
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
