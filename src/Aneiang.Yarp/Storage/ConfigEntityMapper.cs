using System.Text.Json;
using Aneiang.Yarp.Models;

namespace Aneiang.Yarp.Storage;

/// <summary>
/// Maps between domain models and storage entities for core persistence.
/// </summary>
public static class ConfigEntityMapper
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    // ── Route ──────────────────────────────────────────────────────────

    public static RouteEntity ToEntity(this DynamicRouteConfig route) => new()
    {
        RouteUid = string.IsNullOrWhiteSpace(route.RouteUid) ? StableUidFromKey("route", route.RouteId) : route.RouteUid,
        RouteId = route.RouteId,
        ClusterId = route.ClusterId,
        MatchPath = route.MatchPath,
        Order = route.Order,
        Transforms = route.Transforms is { Count: > 0 } ? JsonSerializer.Serialize(route.Transforms, _jsonOptions) : null,
        Source = route.Source,
        CreatedBy = route.CreatedBy,
        CreatedAt = route.CreatedAt,
        UpdatedAt = DateTime.UtcNow,
        Metadata = route.Metadata is { Count: > 0 } ? JsonSerializer.Serialize(route.Metadata, _jsonOptions) : null
    };

    public static DynamicRouteConfig ToRouteConfig(this RouteEntity entity) => new()
    {
        RouteUid = entity.RouteUid,
        RouteId = entity.RouteId,
        ClusterUid = entity.ClusterUid,
        ClusterId = entity.ClusterId,
        MatchPath = entity.MatchPath,
        Order = entity.Order,
        Transforms = string.IsNullOrEmpty(entity.Transforms) ? null : JsonSerializer.Deserialize<List<Dictionary<string, string>>>(entity.Transforms, _jsonOptions),
        Source = entity.Source,
        CreatedBy = entity.CreatedBy,
        CreatedAt = entity.CreatedAt,
        Metadata = string.IsNullOrEmpty(entity.Metadata) ? new() : JsonSerializer.Deserialize<Dictionary<string, string>>(entity.Metadata, _jsonOptions) ?? new()
    };

    public static List<DynamicRouteConfig> ToRouteConfigs(this IEnumerable<RouteEntity> entities)
        => entities.Select(e => e.ToRouteConfig()).ToList();

    // ── Cluster ────────────────────────────────────────────────────────

    public static ClusterEntity ToEntity(this DynamicClusterConfig cluster) => new()
    {
        ClusterUid = string.IsNullOrWhiteSpace(cluster.ClusterUid) ? StableUidFromKey("cluster", cluster.ClusterId) : cluster.ClusterUid,
        ClusterId = cluster.ClusterId,
        LoadBalancingPolicy = cluster.LoadBalancingPolicy,
        HealthCheckConfig = cluster.HealthCheck != null ? JsonSerializer.Serialize(cluster.HealthCheck, _jsonOptions) : null,
        CircuitBreakerConfig = cluster.CircuitBreaker != null ? JsonSerializer.Serialize(cluster.CircuitBreaker, _jsonOptions) : null,
        Source = cluster.Source,
        CreatedBy = cluster.CreatedBy,
        CreatedAt = cluster.CreatedAt,
        UpdatedAt = DateTime.UtcNow,
        LastHeartbeat = cluster.LastHeartbeat
    };

    public static DynamicClusterConfig ToClusterConfig(this ClusterEntity entity) => new()
    {
        ClusterUid = entity.ClusterUid,
        ClusterId = entity.ClusterId,
        LoadBalancingPolicy = entity.LoadBalancingPolicy,
        HealthCheck = string.IsNullOrEmpty(entity.HealthCheckConfig) ? null : JsonSerializer.Deserialize<HealthCheckConfig>(entity.HealthCheckConfig, _jsonOptions),
        CircuitBreaker = string.IsNullOrEmpty(entity.CircuitBreakerConfig) ? null : JsonSerializer.Deserialize<CircuitBreakerConfig>(entity.CircuitBreakerConfig, _jsonOptions),
        Source = entity.Source,
        CreatedBy = entity.CreatedBy,
        CreatedAt = entity.CreatedAt,
        LastHeartbeat = entity.LastHeartbeat,
        Destinations = new Dictionary<string, string>()
    };

    public static List<DynamicClusterConfig> ToClusterConfigs(this IEnumerable<ClusterEntity> entities)
        => entities.Select(e => e.ToClusterConfig()).ToList();

    // ── Destination ────────────────────────────────────────────────────

    public static DestinationEntity ToEntity(this KeyValuePair<string, string> dest, string clusterId) => new()
    {
        DestinationId = dest.Key,
        ClusterId = clusterId,
        Address = dest.Value,
        Healthy = true
    };

    public static Dictionary<string, string> ToDestinations(this IEnumerable<DestinationEntity> entities)
        => entities.ToDictionary(e => e.DestinationId, e => e.Address);

    // ── AuditLog ───────────────────────────────────────────────────────

    public static AuditLogEntity ToEntity(this ConfigChangeAudit audit) => new()
    {
        Id = audit.Id,
        Action = audit.Action,
        Target = audit.Target,
        Operator = audit.Operator,
        ClientIp = audit.ClientIp,
        BeforeData = audit.Before,
        AfterData = audit.After,
        Success = audit.Success,
        ErrorMessage = audit.ErrorMessage,
        Timestamp = audit.Timestamp
    };

    public static ConfigChangeAudit ToConfigChangeAudit(this AuditLogEntity entity) => new()
    {
        Id = entity.Id,
        Action = entity.Action,
        Target = entity.Target,
        Operator = entity.Operator,
        ClientIp = entity.ClientIp,
        Before = entity.BeforeData,
        After = entity.AfterData,
        Success = entity.Success,
        ErrorMessage = entity.ErrorMessage,
        Timestamp = entity.Timestamp
    };

    public static List<ConfigChangeAudit> ToConfigChangeAudits(this IEnumerable<AuditLogEntity> entities)
        => entities.Select(e => e.ToConfigChangeAudit()).ToList();

    private static string StableUidFromKey(string prefix, string key)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(prefix + ":" + key));
        return Convert.ToHexString(bytes, 0, 16).ToLowerInvariant();
    }
}
