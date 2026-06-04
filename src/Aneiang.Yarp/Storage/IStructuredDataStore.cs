namespace Aneiang.Yarp.Storage;

/// <summary>
/// Structured data store interface for relational table operations.
/// Provides CRUD operations for gateway entities with proper table schema.
/// </summary>
public interface IStructuredDataStore : IAsyncDisposable, IDisposable
{
    /// <summary>Initialize database schema (create tables if not exists).</summary>
    Task InitializeAsync(CancellationToken ct = default);

    // ========== YARP Routes ==========
    Task<RouteEntity?> GetRouteAsync(string routeId, CancellationToken ct = default);
    Task<IReadOnlyList<RouteEntity>> GetAllRoutesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<RouteEntity>> GetRoutesByClusterAsync(string clusterId, CancellationToken ct = default);
    Task SaveRouteAsync(RouteEntity route, CancellationToken ct = default);
    Task SaveRoutesAsync(IEnumerable<RouteEntity> routes, CancellationToken ct = default);
    Task DeleteRouteAsync(string routeId, CancellationToken ct = default);
    Task DeleteRoutesByClusterAsync(string clusterId, CancellationToken ct = default);

    // ========== YARP Clusters ==========
    Task<ClusterEntity?> GetClusterAsync(string clusterId, CancellationToken ct = default);
    Task<IReadOnlyList<ClusterEntity>> GetAllClustersAsync(CancellationToken ct = default);
    Task SaveClusterAsync(ClusterEntity cluster, CancellationToken ct = default);
    Task SaveClustersAsync(IEnumerable<ClusterEntity> clusters, CancellationToken ct = default);
    Task DeleteClusterAsync(string clusterId, CancellationToken ct = default);

    // ========== YARP Destinations ==========
    Task<IReadOnlyList<DestinationEntity>> GetDestinationsAsync(string clusterId, CancellationToken ct = default);
    Task SaveDestinationsAsync(string clusterId, IEnumerable<DestinationEntity> destinations, CancellationToken ct = default);
    Task DeleteDestinationsAsync(string clusterId, CancellationToken ct = default);

    // ========== Config History ==========
    Task<ConfigHistoryEntity?> GetConfigHistoryAsync(string versionId, CancellationToken ct = default);
    Task<IReadOnlyList<ConfigHistoryEntity>> GetConfigHistoryListAsync(int limit = 50, CancellationToken ct = default);
    Task SaveConfigHistoryAsync(ConfigHistoryEntity history, CancellationToken ct = default);
    Task DeleteConfigHistoryAsync(string versionId, CancellationToken ct = default);
    Task DeleteOldConfigHistoryAsync(int keepCount, CancellationToken ct = default);

    // ========== Gateway Policies ==========
    Task<PolicyEntity?> GetPolicyAsync(string policyId, CancellationToken ct = default);
    Task<IReadOnlyList<PolicyEntity>> GetAllPoliciesAsync(CancellationToken ct = default);
    Task SavePolicyAsync(PolicyEntity policy, CancellationToken ct = default);
    Task DeletePolicyAsync(string policyId, CancellationToken ct = default);

    // ========== Config Audit Logs ==========
    Task<IReadOnlyList<AuditLogEntity>> GetAuditLogsAsync(int limit = 200, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLogEntity>> GetAuditLogsByTargetAsync(string target, int limit = 50, CancellationToken ct = default);
    Task SaveAuditLogAsync(AuditLogEntity audit, CancellationToken ct = default);
    Task DeleteOldAuditLogsAsync(int keepCount, CancellationToken ct = default);

    // ========== Webhook Settings ==========
    Task<WebhookSettingsEntity?> GetWebhookSettingsAsync(CancellationToken ct = default);
    Task SaveWebhookSettingsAsync(WebhookSettingsEntity settings, CancellationToken ct = default);
}

// ========== Entity Classes ==========

/// <summary>YARP Route entity for database storage.</summary>
public class RouteEntity
{
    public string RouteId { get; set; } = string.Empty;
    public string ClusterId { get; set; } = string.Empty;
    public string MatchPath { get; set; } = string.Empty;
    public int Order { get; set; } = 50;
    public string? Transforms { get; set; } // JSON
    public string Source { get; set; } = "dynamic";
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? Metadata { get; set; } // JSON
    public bool Enabled { get; set; } = true;
}

/// <summary>YARP Cluster entity for database storage.</summary>
public class ClusterEntity
{
    public string ClusterId { get; set; } = string.Empty;
    public string? LoadBalancingPolicy { get; set; }
    public string? HealthCheckConfig { get; set; } // JSON
    public string Source { get; set; } = "dynamic";
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastHeartbeat { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>YARP Destination entity for database storage.</summary>
public class DestinationEntity
{
    public string DestinationId { get; set; } = string.Empty;
    public string ClusterId { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? Host { get; set; }
    public bool Healthy { get; set; } = true;
    public string? Metadata { get; set; } // JSON
}

/// <summary>Config History entity for database storage.</summary>
public class ConfigHistoryEntity
{
    public string VersionId { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ClientIp { get; set; }
    public string ConfigData { get; set; } = "{}"; // Full JSON config
    public string? DiffData { get; set; } // JSON diff
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Gateway Policy entity for database storage.</summary>
public class PolicyEntity
{
    public string PolicyId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Priority { get; set; } = 50;
    public bool Enabled { get; set; } = true;
    public string? CircuitBreakerConfig { get; set; } // JSON
    public string? RetryConfig { get; set; } // JSON
    public string? RateLimitConfig { get; set; } // JSON
    public string? WafConfig { get; set; } // JSON
    public string? CustomPlugins { get; set; } // JSON
    public string? Tags { get; set; } // JSON array
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Config Audit Log entity for database storage.</summary>
public class AuditLogEntity
{
    public string Id { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string? TargetType { get; set; } // Route, Cluster, Policy, etc.
    public string? Operator { get; set; }
    public string? ClientIp { get; set; }
    public string? BeforeData { get; set; } // JSON
    public string? AfterData { get; set; } // JSON
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>Webhook Settings entity for database storage.</summary>
public class WebhookSettingsEntity
{
    public bool Enabled { get; set; }
    public string? Endpoints { get; set; } // JSON array
    public string? Events { get; set; } // JSON array
    public int TimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 3;
    public string? Secret { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
