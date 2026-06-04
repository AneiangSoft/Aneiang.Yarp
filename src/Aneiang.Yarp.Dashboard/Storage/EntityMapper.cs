using System.Text.Json;
using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Storage;

namespace Aneiang.Yarp.Dashboard.Storage;

/// <summary>
/// Maps between domain models and database entities.
/// </summary>
public static class EntityMapper
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    // ========== Route Mapping ==========

    public static RouteEntity ToEntity(this DynamicRouteConfig route)
    {
        return new RouteEntity
        {
            RouteId = route.RouteId,
            ClusterId = route.ClusterId,
            MatchPath = route.MatchPath,
            Order = route.Order,
            Transforms = route.Transforms is { Count: > 0 } ? JsonSerializer.Serialize(route.Transforms, _jsonOptions) : null,
            Source = route.Source,
            CreatedBy = route.CreatedBy,
            CreatedAt = route.CreatedAt,
            UpdatedAt = DateTime.UtcNow,
            Metadata = route.Metadata is { Count: > 0 } ? JsonSerializer.Serialize(route.Metadata, _jsonOptions) : null,
            Enabled = true
        };
    }

    public static DynamicRouteConfig ToRouteConfig(this RouteEntity entity)
    {
        return new DynamicRouteConfig
        {
            RouteId = entity.RouteId,
            ClusterId = entity.ClusterId,
            MatchPath = entity.MatchPath,
            Order = entity.Order,
            Transforms = string.IsNullOrEmpty(entity.Transforms) ? null : JsonSerializer.Deserialize<List<Dictionary<string, string>>>(entity.Transforms, _jsonOptions),
            Source = entity.Source,
            CreatedBy = entity.CreatedBy,
            CreatedAt = entity.CreatedAt,
            Metadata = string.IsNullOrEmpty(entity.Metadata) ? new() : JsonSerializer.Deserialize<Dictionary<string, string>>(entity.Metadata, _jsonOptions) ?? new()
        };
    }

    public static List<DynamicRouteConfig> ToRouteConfigs(this IEnumerable<RouteEntity> entities)
    {
        return entities.Select(e => e.ToRouteConfig()).ToList();
    }

    // ========== Cluster Mapping ==========

    public static ClusterEntity ToEntity(this DynamicClusterConfig cluster)
    {
        return new ClusterEntity
        {
            ClusterId = cluster.ClusterId,
            LoadBalancingPolicy = cluster.LoadBalancingPolicy,
            HealthCheckConfig = cluster.HealthCheck != null ? JsonSerializer.Serialize(cluster.HealthCheck, _jsonOptions) : null,
            Source = cluster.Source,
            CreatedBy = cluster.CreatedBy,
            CreatedAt = cluster.CreatedAt,
            UpdatedAt = DateTime.UtcNow,
            LastHeartbeat = cluster.LastHeartbeat,
            Enabled = true
        };
    }

    public static DynamicClusterConfig ToClusterConfig(this ClusterEntity entity)
    {
        return new DynamicClusterConfig
        {
            ClusterId = entity.ClusterId,
            LoadBalancingPolicy = entity.LoadBalancingPolicy,
            HealthCheck = string.IsNullOrEmpty(entity.HealthCheckConfig) ? null : JsonSerializer.Deserialize<HealthCheckConfig>(entity.HealthCheckConfig, _jsonOptions),
            Source = entity.Source,
            CreatedBy = entity.CreatedBy,
            CreatedAt = entity.CreatedAt,
            LastHeartbeat = entity.LastHeartbeat,
            Destinations = new Dictionary<string, string>() // Populated separately
        };
    }

    public static List<DynamicClusterConfig> ToClusterConfigs(this IEnumerable<ClusterEntity> entities)
    {
        return entities.Select(e => e.ToClusterConfig()).ToList();
    }

    // ========== Destination Mapping ==========

    public static DestinationEntity ToEntity(this KeyValuePair<string, string> dest, string clusterId)
    {
        return new DestinationEntity
        {
            DestinationId = dest.Key,
            ClusterId = clusterId,
            Address = dest.Value,
            Healthy = true
        };
    }

    public static Dictionary<string, string> ToDestinations(this IEnumerable<DestinationEntity> entities)
    {
        return entities.ToDictionary(e => e.DestinationId, e => e.Address);
    }

    // ========== Policy Mapping ==========

    public static PolicyEntity ToEntity(this GatewayPolicy policy)
    {
        return new PolicyEntity
        {
            PolicyId = policy.PolicyId,
            DisplayName = policy.DisplayName,
            Description = policy.Description,
            Priority = policy.Priority,
            Enabled = policy.Enabled,
            CircuitBreakerConfig = policy.CircuitBreaker != null ? JsonSerializer.Serialize(policy.CircuitBreaker, _jsonOptions) : null,
            RetryConfig = policy.Retry != null ? JsonSerializer.Serialize(policy.Retry, _jsonOptions) : null,
            RateLimitConfig = policy.RateLimit != null ? JsonSerializer.Serialize(policy.RateLimit, _jsonOptions) : null,
            WafConfig = policy.Waf != null ? JsonSerializer.Serialize(policy.Waf, _jsonOptions) : null,
            CustomPlugins = policy.CustomPlugins != null ? JsonSerializer.Serialize(policy.CustomPlugins, _jsonOptions) : null,
            Tags = policy.Tags is { Count: > 0 } ? JsonSerializer.Serialize(policy.Tags, _jsonOptions) : null,
            CreatedBy = policy.CreatedBy,
            CreatedAt = policy.CreatedAt,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public static GatewayPolicy ToGatewayPolicy(this PolicyEntity entity)
    {
        return new GatewayPolicy
        {
            PolicyId = entity.PolicyId,
            DisplayName = entity.DisplayName,
            Description = entity.Description,
            Priority = entity.Priority,
            Enabled = entity.Enabled,
            CircuitBreaker = string.IsNullOrEmpty(entity.CircuitBreakerConfig) ? null : JsonSerializer.Deserialize<PolicyCircuitBreaker>(entity.CircuitBreakerConfig, _jsonOptions),
            Retry = string.IsNullOrEmpty(entity.RetryConfig) ? null : JsonSerializer.Deserialize<PolicyRetry>(entity.RetryConfig, _jsonOptions),
            RateLimit = string.IsNullOrEmpty(entity.RateLimitConfig) ? null : JsonSerializer.Deserialize<PolicyRateLimit>(entity.RateLimitConfig, _jsonOptions),
            Waf = string.IsNullOrEmpty(entity.WafConfig) ? null : JsonSerializer.Deserialize<PolicyWaf>(entity.WafConfig, _jsonOptions),
            CustomPlugins = string.IsNullOrEmpty(entity.CustomPlugins) ? null : JsonSerializer.Deserialize<Dictionary<string, object>>(entity.CustomPlugins, _jsonOptions),
            Tags = string.IsNullOrEmpty(entity.Tags) ? new() : JsonSerializer.Deserialize<List<string>>(entity.Tags, _jsonOptions) ?? new(),
            CreatedBy = entity.CreatedBy,
            CreatedAt = entity.CreatedAt
        };
    }

    public static List<GatewayPolicy> ToGatewayPolicies(this IEnumerable<PolicyEntity> entities)
    {
        return entities.Select(e => e.ToGatewayPolicy()).ToList();
    }

    // ========== Audit Log Mapping ==========

    public static AuditLogEntity ToEntity(this ConfigChangeAudit audit)
    {
        return new AuditLogEntity
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
    }

    public static ConfigChangeAudit ToConfigChangeAudit(this AuditLogEntity entity)
    {
        return new ConfigChangeAudit
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
    }

    public static List<ConfigChangeAudit> ToConfigChangeAudits(this IEnumerable<AuditLogEntity> entities)
    {
        return entities.Select(e => e.ToConfigChangeAudit()).ToList();
    }

    // ========== Config History Mapping ==========

    public static ConfigHistoryEntity ToEntity(this ConfigSnapshot snapshot, string? createdBy = null)
    {
        return new ConfigHistoryEntity
        {
            VersionId = snapshot.VersionId,
            Description = snapshot.Description,
            ClientIp = snapshot.ClientIp,
            ConfigData = snapshot.Config.ToString(),
            CreatedBy = createdBy ?? "system",
            CreatedAt = snapshot.Timestamp
        };
    }

    public static ConfigSnapshot ToConfigSnapshot(this ConfigHistoryEntity entity)
    {
        return new ConfigSnapshot
        {
            VersionId = entity.VersionId,
            Description = entity.Description,
            ClientIp = entity.ClientIp,
            Config = JsonSerializer.Deserialize<JsonElement>(entity.ConfigData),
            Timestamp = entity.CreatedAt
        };
    }

    public static List<ConfigSnapshot> ToConfigSnapshots(this IEnumerable<ConfigHistoryEntity> entities)
    {
        return entities.Select(e => e.ToConfigSnapshot()).ToList();
    }
}
