using System.Text.Json;
using Aneiang.Yarp.Models;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Storage;

public static class ConfigEntityMapper
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static RouteEntity ToEntity(this DynamicRouteConfig route) => new()
    {
        RouteUid = string.IsNullOrWhiteSpace(route.RouteUid) ? StableUid.FromKey("route", route.Config.RouteId ?? string.Empty) : route.RouteUid,
        RouteId = route.Config.RouteId ?? string.Empty,
        ClusterUid = route.ClusterUid,
        ClusterId = route.Config.ClusterId ?? string.Empty,
        MatchPath = route.Config.Match?.Path ?? string.Empty,
        Order = route.Config.Order ?? int.MaxValue,
        Transforms = route.Config.Transforms is { Count: > 0 } ? JsonSerializer.Serialize(route.Config.Transforms, _jsonOptions) : null,
        Source = route.Source,
        CreatedBy = route.CreatedBy,
        CreatedAt = route.CreatedAt,
        UpdatedAt = DateTime.Now,
        Metadata = route.Metadata is { Count: > 0 } ? JsonSerializer.Serialize(route.Metadata, _jsonOptions) : null,
        ConfigJson = Serialization.YarpJsonConfig.SerializeRoute(route.Config)
    };

    public static DynamicRouteConfig ToRouteConfig(this RouteEntity entity)
    {
        var route = new DynamicRouteConfig
        {
            Config = BuildRouteConfig(entity),
            RouteUid = entity.RouteUid,
            ClusterUid = entity.ClusterUid,
            Source = entity.Source,
            CreatedBy = entity.CreatedBy,
            CreatedAt = entity.CreatedAt,
            Metadata = string.IsNullOrEmpty(entity.Metadata)
                ? new()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(entity.Metadata, _jsonOptions) ?? new()
        };
        return route;
    }

    private static RouteConfig BuildRouteConfig(RouteEntity entity)
    {
        if (!string.IsNullOrEmpty(entity.ConfigJson))
        {
            var parsed = Serialization.YarpJsonConfig.DeserializeRoute(entity.ConfigJson);
            if (parsed != null)
            {
                var normalized = parsed.Order == null ? parsed with { Order = int.MaxValue } : parsed;
                return normalized with { RouteId = entity.RouteId, ClusterId = entity.ClusterId };
            }
        }

        return new RouteConfig
        {
            RouteId = entity.RouteId,
            ClusterId = entity.ClusterId,
            Match = new RouteMatch { Path = entity.MatchPath },
            Order = entity.Order,
            Transforms = string.IsNullOrEmpty(entity.Transforms)
                ? null
                : JsonSerializer.Deserialize<List<Dictionary<string, string>>>(entity.Transforms, _jsonOptions)
                    ?.Select(t => (IReadOnlyDictionary<string, string>)t).ToList()
        };
    }

    public static List<DynamicRouteConfig> ToRouteConfigs(this IEnumerable<RouteEntity> entities)
        => entities.Select(e => e.ToRouteConfig()).ToList();

    public static ClusterEntity ToEntity(this DynamicClusterConfig cluster) => new()
    {
        ClusterUid = string.IsNullOrWhiteSpace(cluster.ClusterUid) ? StableUid.FromKey("cluster", cluster.Config.ClusterId ?? string.Empty) : cluster.ClusterUid,
        ClusterId = cluster.Config.ClusterId ?? string.Empty,
        LoadBalancingPolicy = cluster.Config.LoadBalancingPolicy,
        HealthCheckConfig = cluster.HealthCheck != null ? JsonSerializer.Serialize(cluster.HealthCheck, _jsonOptions) : null,
        CircuitBreakerConfig = cluster.CircuitBreaker != null ? JsonSerializer.Serialize(cluster.CircuitBreaker, _jsonOptions) : null,
        Source = cluster.Source,
        CreatedBy = cluster.CreatedBy,
        CreatedAt = cluster.CreatedAt,
        UpdatedAt = DateTime.Now,
        LastHeartbeat = cluster.LastHeartbeat,
        ConfigJson = Serialization.YarpJsonConfig.SerializeCluster(cluster.Config)
    };

    public static DynamicClusterConfig ToClusterConfig(this ClusterEntity entity)
    {
        return new DynamicClusterConfig
        {
            Config = BuildClusterConfig(entity),
            ClusterUid = entity.ClusterUid,
            HealthCheck = string.IsNullOrEmpty(entity.HealthCheckConfig) ? null : JsonSerializer.Deserialize<Models.HealthCheckConfig>(entity.HealthCheckConfig, _jsonOptions),
            CircuitBreaker = string.IsNullOrEmpty(entity.CircuitBreakerConfig) ? null : JsonSerializer.Deserialize<CircuitBreakerConfig>(entity.CircuitBreakerConfig, _jsonOptions),
            Source = entity.Source,
            CreatedBy = entity.CreatedBy,
            CreatedAt = entity.CreatedAt,
            LastHeartbeat = entity.LastHeartbeat
        };
    }

    private static ClusterConfig BuildClusterConfig(ClusterEntity entity)
    {
        if (!string.IsNullOrEmpty(entity.ConfigJson))
        {
            var parsed = Serialization.YarpJsonConfig.DeserializeCluster(entity.ConfigJson);
            if (parsed != null)
                return parsed with { ClusterId = entity.ClusterId };
        }

        return new ClusterConfig
        {
            ClusterId = entity.ClusterId,
            LoadBalancingPolicy = entity.LoadBalancingPolicy
        };
    }

    public static List<DynamicClusterConfig> ToClusterConfigs(this IEnumerable<ClusterEntity> entities)
        => entities.Select(e => e.ToClusterConfig()).ToList();

    public static DestinationEntity ToEntity(this KeyValuePair<string, string> dest, string clusterId) => new()
    {
        DestinationId = dest.Key,
        ClusterId = clusterId,
        Address = dest.Value,
        Healthy = true
    };

    public static Dictionary<string, string> ToDestinations(this IEnumerable<DestinationEntity> entities)
        => entities.ToDictionary(e => e.DestinationId, e => e.Address);

    public static AuditLogEntity ToEntity(this ConfigChangeAudit audit) => new()
    {
        Id = audit.Id,
        Action = audit.Action,
        Target = audit.Target,
        TargetType = audit.TargetType,
        TargetUid = audit.TargetUid,
        TargetKeySnapshot = audit.TargetKeySnapshot ?? audit.Target,
        TargetDisplayNameSnapshot = audit.TargetDisplayNameSnapshot,
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
}
