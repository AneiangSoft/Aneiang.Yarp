using System.Text.Json;
using Aneiang.Yarp.Dashboard.Modules.Policy.Models;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Storage;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Storage;

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
            Metadata = route.Metadata is { Count: > 0 } ? JsonSerializer.Serialize(route.Metadata, _jsonOptions) : null
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
            CircuitBreakerConfig = cluster.CircuitBreaker != null ? JsonSerializer.Serialize(cluster.CircuitBreaker, _jsonOptions) : null,
            Source = cluster.Source,
            CreatedBy = cluster.CreatedBy,
            CreatedAt = cluster.CreatedAt,
            UpdatedAt = DateTime.UtcNow,
            LastHeartbeat = cluster.LastHeartbeat
        };
    }

    public static DynamicClusterConfig ToClusterConfig(this ClusterEntity entity)
    {
        return new DynamicClusterConfig
        {
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

    public static PolicyEntity ToEntity(this RoutePolicy policy)
    {
        return new PolicyEntity
        {
            PolicyId = policy.PolicyId,
            PolicyType = "route",
            DisplayName = policy.DisplayName,
            Description = policy.Description,
            Enabled = policy.Enabled,
            RetryConfig = policy.Retry != null ? JsonSerializer.Serialize(policy.Retry, _jsonOptions) : null,
            RateLimitConfig = policy.RateLimit != null ? JsonSerializer.Serialize(policy.RateLimit, _jsonOptions) : null,
            WafEnabled = policy.WafEnabled?.ToString().ToLowerInvariant(),
            AppliedTargets = policy.AppliedRoutes is { Count: > 0 } ? JsonSerializer.Serialize(policy.AppliedRoutes, _jsonOptions) : null,
            CreatedAt = policy.CreatedAt,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public static PolicyEntity ToEntity(this ClusterPolicy policy)
    {
        return new PolicyEntity
        {
            PolicyId = policy.PolicyId,
            PolicyType = "cluster",
            DisplayName = policy.DisplayName,
            Description = policy.Description,
            Enabled = policy.Enabled,
            CircuitBreakerConfig = policy.CircuitBreaker != null ? JsonSerializer.Serialize(policy.CircuitBreaker, _jsonOptions) : null,
            AppliedTargets = policy.AppliedClusters is { Count: > 0 } ? JsonSerializer.Serialize(policy.AppliedClusters, _jsonOptions) : null,
            CreatedAt = policy.CreatedAt,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public static RoutePolicy ToRoutePolicy(this PolicyEntity entity)
    {
        return new RoutePolicy
        {
            PolicyId = entity.PolicyId,
            DisplayName = entity.DisplayName,
            Description = entity.Description,
            Enabled = entity.Enabled,
            Retry = string.IsNullOrEmpty(entity.RetryConfig) ? null : JsonSerializer.Deserialize<PolicyRetry>(entity.RetryConfig, _jsonOptions),
            RateLimit = string.IsNullOrEmpty(entity.RateLimitConfig) ? null : JsonSerializer.Deserialize<PolicyRateLimit>(entity.RateLimitConfig, _jsonOptions),
            WafEnabled = entity.WafEnabled switch
            {
                "true" => true,
                "false" => false,
                _ => null
            },
            AppliedRoutes = string.IsNullOrEmpty(entity.AppliedTargets)
                ? new()
                : JsonSerializer.Deserialize<List<string>>(entity.AppliedTargets, _jsonOptions) ?? new(),
            CreatedAt = entity.CreatedAt
        };
    }

    public static ClusterPolicy ToClusterPolicy(this PolicyEntity entity)
    {
        return new ClusterPolicy
        {
            PolicyId = entity.PolicyId,
            DisplayName = entity.DisplayName,
            Description = entity.Description,
            Enabled = entity.Enabled,
            CircuitBreaker = string.IsNullOrEmpty(entity.CircuitBreakerConfig)
                ? null
                : JsonSerializer.Deserialize<PolicyCircuitBreaker>(entity.CircuitBreakerConfig, _jsonOptions),
            AppliedClusters = string.IsNullOrEmpty(entity.AppliedTargets)
                ? new()
                : JsonSerializer.Deserialize<List<string>>(entity.AppliedTargets, _jsonOptions) ?? new(),
            CreatedAt = entity.CreatedAt
        };
    }

    public static List<RoutePolicy> ToRoutePolicies(this IEnumerable<PolicyEntity> entities)
    {
        return entities.Where(e => e.PolicyType == "route").Select(e => e.ToRoutePolicy()).ToList();
    }

    public static List<ClusterPolicy> ToClusterPolicies(this IEnumerable<PolicyEntity> entities)
    {
        return entities.Where(e => e.PolicyType == "cluster").Select(e => e.ToClusterPolicy()).ToList();
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
