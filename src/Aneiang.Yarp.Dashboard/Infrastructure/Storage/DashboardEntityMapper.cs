using System.Text.Json;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Aneiang.Yarp.Dashboard.Modules.Policy.Models;
using Aneiang.Yarp.Storage;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Storage;

/// <summary>
/// Dashboard-specific entity mapping extensions for Policy and ConfigSnapshot types.
/// These types live in the Dashboard assembly and cannot be mapped from the core library.
/// Route/Cluster/Destination/Audit mappings are in <see cref="ConfigEntityMapper"/>.
/// </summary>
internal static class DashboardEntityMapper
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    #region Policy

    public static PolicyEntity ToEntity(this RoutePolicy policy) => new()
    {
        PolicyUid = string.IsNullOrWhiteSpace(policy.PolicyId) ? Guid.NewGuid().ToString("N") : StableUid.FromKey("policy", policy.PolicyId),
        PolicyId = policy.PolicyId,
        PolicyType = "route",
        DisplayName = policy.DisplayName,
        Description = policy.Description,
        Enabled = policy.Enabled,
        RetryConfig = policy.Retry != null ? JsonSerializer.Serialize(policy.Retry, _jsonOptions) : null,
        RateLimitConfig = policy.RateLimit != null ? JsonSerializer.Serialize(policy.RateLimit, _jsonOptions) : null,
        WafEnabled = policy.WafEnabled?.ToString().ToLowerInvariant(),
        CreatedAt = policy.CreatedAt,
        UpdatedAt = DateTime.Now
    };

    public static PolicyEntity ToEntity(this ClusterPolicy policy) => new()
    {
        PolicyUid = string.IsNullOrWhiteSpace(policy.PolicyId) ? Guid.NewGuid().ToString("N") : StableUid.FromKey("policy", policy.PolicyId),
        PolicyId = policy.PolicyId,
        PolicyType = "cluster",
        DisplayName = policy.DisplayName,
        Description = policy.Description,
        Enabled = policy.Enabled,
        CircuitBreakerConfig = policy.CircuitBreaker != null ? JsonSerializer.Serialize(policy.CircuitBreaker, _jsonOptions) : null,
        CreatedAt = policy.CreatedAt,
        UpdatedAt = DateTime.Now
    };

    public static RoutePolicy ToRoutePolicy(this PolicyEntity entity) => new()
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
        AppliedRoutes = new(),
        CreatedAt = entity.CreatedAt
    };

    public static ClusterPolicy ToClusterPolicy(this PolicyEntity entity) => new()
    {
        PolicyUid = entity.PolicyUid,
        PolicyId = entity.PolicyId,
        DisplayName = entity.DisplayName,
        Description = entity.Description,
        Enabled = entity.Enabled,
        CircuitBreaker = string.IsNullOrEmpty(entity.CircuitBreakerConfig)
            ? null
            : JsonSerializer.Deserialize<PolicyCircuitBreaker>(entity.CircuitBreakerConfig, _jsonOptions),
        AppliedClusters = new(),
        CreatedAt = entity.CreatedAt
    };

    public static List<RoutePolicy> ToRoutePolicies(this IEnumerable<PolicyEntity> entities)
        => entities.Where(e => e.PolicyType == "route").Select(e => e.ToRoutePolicy()).ToList();

    public static List<ClusterPolicy> ToClusterPolicies(this IEnumerable<PolicyEntity> entities)
        => entities.Where(e => e.PolicyType == "cluster").Select(e => e.ToClusterPolicy()).ToList();

    #endregion

    #region ConfigSnapshot

    public static ConfigHistoryEntity ToEntity(this ConfigSnapshot snapshot, string? createdBy = null) => new()
    {
        VersionId = snapshot.VersionId,
        Description = snapshot.Description,
        ClientIp = snapshot.ClientIp,
        ConfigData = snapshot.Config.ToString(),
        CreatedBy = createdBy ?? "system",
        CreatedAt = snapshot.Timestamp
    };

    public static ConfigSnapshot ToConfigSnapshot(this ConfigHistoryEntity entity) => new()
    {
        VersionId = entity.VersionId,
        Description = entity.Description,
        ClientIp = entity.ClientIp,
        Config = JsonSerializer.Deserialize<JsonElement>(entity.ConfigData),
        Timestamp = entity.CreatedAt
    };

    public static List<ConfigSnapshot> ToConfigSnapshots(this IEnumerable<ConfigHistoryEntity> entities)
        => entities.Select(e => e.ToConfigSnapshot()).ToList();

    #endregion
}
