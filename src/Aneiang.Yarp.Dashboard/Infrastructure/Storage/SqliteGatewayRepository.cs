using System.Text.Json;
using Aneiang.Yarp.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Storage;

/// <summary>
/// Unified SQLite gateway repository implementing <see cref="IGatewayRepository"/>.
/// Contains all tables in one schema initialization and delegates to internal helper methods.
/// </summary>
public sealed class SqliteGatewayRepository : IGatewayRepository
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _connectionString;
    private readonly ILogger<SqliteGatewayRepository> _logger;
    private bool _initialized;
    private static bool _providerSet;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public SqliteGatewayRepository(StorageOptions options, ILogger<SqliteGatewayRepository> logger)
    {
        _connectionString = EnsurePoolingEnabled(options.Sqlite.ConnectionString);
        _logger = logger;
        EnsureProvider();
    }

    private static void EnsureProvider()
    {
        if (_providerSet) return;
        SQLitePCL.Batteries_V2.Init();
        _providerSet = true;
    }

    private static string EnsurePoolingEnabled(string cs)
    {
        if (string.IsNullOrWhiteSpace(cs)) return "Data Source=gateway-store.db;Pooling=true";
        return cs.Contains("Pooling=", StringComparison.OrdinalIgnoreCase) ? cs : cs.TrimEnd(';') + ";Pooling=true";
    }

    private SqliteConnection CreateConnection() => new(_connectionString);

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;
        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            await InitializeAsync(ct);
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await using var conn = CreateConnection();
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
                metadata TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_routes_cluster ON yarp_routes(cluster_id);

            -- Clusters table
            CREATE TABLE IF NOT EXISTS yarp_clusters (
                cluster_id TEXT PRIMARY KEY,
                load_balancing_policy TEXT,
                health_check_config TEXT,
                source TEXT DEFAULT 'dynamic',
                created_by TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                last_heartbeat TEXT
            );

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

            -- Proxy logs table
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
            CREATE INDEX IF NOT EXISTS ix_proxy_logs_timestamp ON proxy_logs(timestamp DESC);
            """;
        await cmd.ExecuteNonQueryAsync(ct);
        _initialized = true;
        _logger.LogInformation("SqliteGatewayRepository initialized");
    }

    // ========== IRouteRepository ==========

    public async Task<RouteEntity?> GetRouteAsync(string routeId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM yarp_routes WHERE route_id = @id";
        cmd.Parameters.AddWithValue("@id", routeId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapRoute(reader) : null;
    }

    public async Task<IReadOnlyList<RouteEntity>> GetAllRoutesAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """SELECT * FROM yarp_routes ORDER BY "order", route_id""";
        var list = new List<RouteEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(MapRoute(reader));
        return list.AsReadOnly();
    }

    public async Task<IReadOnlyList<RouteEntity>> GetRoutesByClusterAsync(string clusterId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """SELECT * FROM yarp_routes WHERE cluster_id = @cid ORDER BY "order" """;
        cmd.Parameters.AddWithValue("@cid", clusterId);
        var list = new List<RouteEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(MapRoute(reader));
        return list.AsReadOnly();
    }

    public async Task SaveRouteAsync(RouteEntity route, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        route.UpdatedAt = DateTime.UtcNow;
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO yarp_routes (route_id, cluster_id, match_path, "order", transforms, source, created_by, created_at, updated_at, metadata)
            VALUES (@id, @cid, @path, @order, @trans, @src, @cb, @ca, @ua, @meta)
            ON CONFLICT(route_id) DO UPDATE SET
                cluster_id = @cid, match_path = @path, "order" = @order, transforms = @trans,
                source = @src, updated_at = @ua, metadata = @meta
            """;
        AddRouteParams(cmd, route);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveRoutesAsync(IEnumerable<RouteEntity> routes, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
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
                AddRouteParams(cmd, r);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch { await tx.RollbackAsync(ct); throw; }
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
    }

    // ========== IClusterRepository ==========

    public async Task<ClusterEntity?> GetClusterAsync(string clusterId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
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
        await using var conn = CreateConnection();
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
        cluster.UpdatedAt = DateTime.UtcNow;
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await SaveClusterInternalAsync(conn, null, cluster, ct);
    }

    public async Task SaveClustersAsync(IEnumerable<ClusterEntity> clusters, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = conn.BeginTransaction();
        try
        {
            foreach (var c in clusters)
            {
                c.UpdatedAt = DateTime.UtcNow;
                await SaveClusterInternalAsync(conn, tx, c, ct);
            }
            await tx.CommitAsync(ct);
        }
        catch { await tx.RollbackAsync(ct); throw; }
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
    }

    public async Task<IReadOnlyList<DestinationEntity>> GetDestinationsAsync(string clusterId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
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
        await using var conn = CreateConnection();
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
                    ON CONFLICT(destination_id) DO UPDATE SET
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
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM yarp_destinations WHERE cluster_id = @cid";
        cmd.Parameters.AddWithValue("@cid", clusterId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ========== IConfigHistoryRepository ==========

    public async Task<ConfigHistoryEntity?> GetConfigHistoryAsync(string versionId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM yarp_config_history WHERE version_id = @id";
        cmd.Parameters.AddWithValue("@id", versionId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapConfigHistory(reader) : null;
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
        while (await reader.ReadAsync(ct)) list.Add(MapConfigHistory(reader));
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
            WHERE version_id NOT IN (SELECT version_id FROM yarp_config_history ORDER BY created_at DESC LIMIT @keep)
            """;
        cmd.Parameters.AddWithValue("@keep", keepCount);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ========== IPolicyRepository ==========

    public async Task<PolicyEntity?> GetPolicyAsync(string policyId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM gateway_policies WHERE policy_id = @id";
        cmd.Parameters.AddWithValue("@id", policyId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapPolicy(reader) : null;
    }

    public async Task<IReadOnlyList<PolicyEntity>> GetAllPoliciesAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM gateway_policies ORDER BY priority DESC, display_name";
        var list = new List<PolicyEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(MapPolicy(reader));
        return list.AsReadOnly();
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
    }

    // ========== IAuditLogRepository ==========

    public async Task<IReadOnlyList<AuditLogEntity>> GetAuditLogsAsync(int limit = 200, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM config_audit_logs ORDER BY timestamp DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);
        var list = new List<AuditLogEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(MapAuditLog(reader));
        return list.AsReadOnly();
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
        var list = new List<AuditLogEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(MapAuditLog(reader));
        return list.AsReadOnly();
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
    }

    public async Task DeleteOldAuditLogsAsync(int keepCount, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM config_audit_logs WHERE id NOT IN (SELECT id FROM config_audit_logs ORDER BY timestamp DESC LIMIT @keep)
            """;
        cmd.Parameters.AddWithValue("@keep", keepCount);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ========== IWebhookSettingsRepository ==========

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
    }

    // ========== IProxyLogRepository ==========

    public async Task SaveProxyLogAsync(ProxyLogEntity log, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO proxy_logs (id, method, path, route_id, cluster_id, destination_id, status_code, duration_ms, request_body_size, response_body_size, client_ip, error_message, timestamp)
            VALUES (@id, @method, @path, @rid, @cid, @did, @status, @dur, @reqsz, @respsz, @ip, @err, @ts)
            """;
        cmd.Parameters.AddWithValue("@id", log.Id);
        cmd.Parameters.AddWithValue("@method", log.Method ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@path", log.Path ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@rid", log.RouteId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@cid", log.ClusterId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@did", log.DestinationId ?? (object)DBNull.Value);
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
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM proxy_logs ORDER BY timestamp DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);
        var list = new List<ProxyLogEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(MapProxyLog(reader));
        return list.AsReadOnly();
    }

    public async Task DeleteOldProxyLogsAsync(int keepCount, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM proxy_logs WHERE id NOT IN (SELECT id FROM proxy_logs ORDER BY timestamp DESC LIMIT @keep)";
        cmd.Parameters.AddWithValue("@keep", keepCount);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ========== IAsyncDisposable / IDisposable ==========

    public void Dispose() => GC.SuppressFinalize(this);
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }

    // ========== Private Mapping Helpers ==========

    private static void AddRouteParams(SqliteCommand cmd, RouteEntity r)
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

    private static RouteEntity MapRoute(SqliteDataReader r) => new()
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

    private static async Task SaveClusterInternalAsync(SqliteConnection conn, SqliteTransaction? tx, ClusterEntity c, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO yarp_clusters (cluster_id, load_balancing_policy, health_check_config, source, created_by, created_at, updated_at, last_heartbeat)
            VALUES (@id, @lb, @hc, @src, @cb, @ca, @ua, @lh)
            ON CONFLICT(cluster_id) DO UPDATE SET
                load_balancing_policy = @lb, health_check_config = @hc, source = @src,
                updated_at = @ua, last_heartbeat = @lh
            """;
        cmd.Parameters.AddWithValue("@id", c.ClusterId);
        cmd.Parameters.AddWithValue("@lb", c.LoadBalancingPolicy ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@hc", c.HealthCheckConfig ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@src", c.Source);
        cmd.Parameters.AddWithValue("@cb", c.CreatedBy ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ca", c.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@ua", c.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@lh", c.LastHeartbeat?.ToString("O") ?? (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static ClusterEntity MapCluster(SqliteDataReader r) => new()
    {
        ClusterId = r.GetString(0),
        LoadBalancingPolicy = r.IsDBNull(1) ? null : r.GetString(1),
        HealthCheckConfig = r.IsDBNull(2) ? null : r.GetString(2),
        Source = r.IsDBNull(3) ? "dynamic" : r.GetString(3),
        CreatedBy = r.IsDBNull(4) ? null : r.GetString(4),
        CreatedAt = r.IsDBNull(5) ? DateTime.MinValue : DateTime.Parse(r.GetString(5)),
        UpdatedAt = r.IsDBNull(6) ? DateTime.MinValue : DateTime.Parse(r.GetString(6)),
        LastHeartbeat = r.IsDBNull(7) ? null : DateTime.Parse(r.GetString(7))
    };

    private static DestinationEntity MapDestination(SqliteDataReader r) => new()
    {
        DestinationId = r.GetString(0),
        ClusterId = r.GetString(1),
        Address = r.GetString(2),
        Host = r.IsDBNull(3) ? null : r.GetString(3),
        Healthy = r.IsDBNull(4) || r.GetInt32(4) == 1,
        Metadata = r.IsDBNull(5) ? null : r.GetString(5)
    };

    private static ConfigHistoryEntity MapConfigHistory(SqliteDataReader r) => new()
    {
        VersionId = r.GetString(0),
        Description = r.IsDBNull(1) ? null : r.GetString(1),
        ClientIp = r.IsDBNull(2) ? null : r.GetString(2),
        ConfigData = r.GetString(3),
        DiffData = r.IsDBNull(4) ? null : r.GetString(4),
        CreatedBy = r.GetString(5),
        CreatedAt = DateTime.Parse(r.GetString(6))
    };

    private static PolicyEntity MapPolicy(SqliteDataReader r) => new()
    {
        PolicyId = r.GetString(0),
        DisplayName = r.GetString(1),
        Description = r.IsDBNull(2) ? null : r.GetString(2),
        Priority = r.GetInt32(3),
        Enabled = r.GetInt32(4) == 1,
        CircuitBreakerConfig = r.IsDBNull(5) ? null : r.GetString(5),
        RetryConfig = r.IsDBNull(6) ? null : r.GetString(6),
        RateLimitConfig = r.IsDBNull(7) ? null : r.GetString(7),
        WafConfig = r.IsDBNull(8) ? null : r.GetString(8),
        CustomPlugins = r.IsDBNull(9) ? null : r.GetString(9),
        Tags = r.IsDBNull(10) ? null : r.GetString(10),
        CreatedBy = r.IsDBNull(11) ? null : r.GetString(11),
        CreatedAt = r.IsDBNull(12) ? DateTime.MinValue : DateTime.Parse(r.GetString(12)),
        UpdatedAt = r.IsDBNull(13) ? DateTime.MinValue : DateTime.Parse(r.GetString(13))
    };

    private static AuditLogEntity MapAuditLog(SqliteDataReader r) => new()
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
        Timestamp = DateTime.Parse(r.GetString(10))
    };

    private static ProxyLogEntity MapProxyLog(SqliteDataReader r) => new()
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
