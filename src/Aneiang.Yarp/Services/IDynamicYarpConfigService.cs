using Aneiang.Yarp.Models;
using Microsoft.Extensions.Configuration;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Interface for dynamic YARP configuration management at runtime.
/// Provides thread-safe operations for routes, clusters, health check, heartbeat, and metadata.
/// </summary>
public interface IDynamicYarpConfigService
{
    /// <summary>Add or update a route (creates or replaces cluster). Thread-safe.</summary>
    Task<RouteOperationResult> TryAddRoute(RegisterRouteRequest request, string source = "dynamic", string? createdBy = null);

    /// <summary>
    /// Add or update a route from a complete native YARP <see cref="RouteConfig"/>, preserving all
    /// advanced properties (full Match criteria, Auth/Cors/RateLimiter/Timeout policies, etc.).
    /// </summary>
    Task<RouteOperationResult> TryAddRouteConfig(RouteConfig route, string source = "dashboard", string? createdBy = "dashboard-user");

    /// <summary>
    /// Add or update a cluster from a complete native YARP <see cref="ClusterConfig"/>, preserving all
    /// advanced properties (SessionAffinity, HttpClient, HttpRequest, per-destination metadata, etc.).
    /// </summary>
    Task<RouteOperationResult> TryAddClusterConfig(ClusterConfig cluster, string source = "dashboard", string? createdBy = "dashboard-user");

    /// <summary>Remove a route and optionally its orphaned cluster.</summary>
    Task<RouteOperationResult> TryRemoveRoute(string routeName, string? clientIp = null, bool removeOrphanedCluster = true);

    /// <summary>Add or update a cluster with destinations.</summary>
    Task<RouteOperationResult> TryAddCluster(string clusterId, Dictionary<string, string> destinations,
        string? loadBalancingPolicy = null, Models.HealthCheckConfig? healthCheck = null,
        string source = "dynamic", string? createdBy = null);

    /// <summary>Add a new cluster from a creation request.</summary>
    Task<RouteOperationResult> TryAddCluster(CreateClusterRequest request, string source = "dynamic", string? createdBy = null);

    /// <summary>Partially update an existing cluster.</summary>
    Task<RouteOperationResult> TryUpdateCluster(string clusterId, UpdateClusterRequest request);

    /// <summary>Remove a cluster if no routes reference it.</summary>
    Task<RouteOperationResult> TryRemoveCluster(string clusterId);

    /// <summary>Atomically rename a cluster and update all referencing routes.</summary>
    Task<RouteOperationResult> TryRenameCluster(string oldClusterId, string newClusterId,
        Dictionary<string, string> destinations, string? loadBalancingPolicy = null,
        Models.HealthCheckConfig? healthCheck = null, string source = "dashboard", string? createdBy = "dashboard-user");

    /// <summary>Update or merge metadata entries on a route. Used by the policy engine.</summary>
    Task<bool> UpdateRouteMetadataAsync(string routeId, Dictionary<string, string> metadata);

    /// <summary>Get all current routes.</summary>
    IReadOnlyList<RouteConfig> GetRoutes();

    /// <summary>Get all current clusters.</summary>
    IReadOnlyList<ClusterConfig> GetClusters();

    /// <summary>Get a specific cluster by ID.</summary>
    ClusterConfig? GetCluster(string clusterId);

    /// <summary>Get dynamic configuration metadata.</summary>
    GatewayDynamicConfig? GetDynamicConfig();

    /// <summary>Re-apply dynamic config to YARP in-memory provider.</summary>
    void RefreshConfig();

    /// <summary>Save dynamic configuration to persistence.</summary>
    Task SaveDynamicConfig();

    /// <summary>Replace entire configuration in one batch operation (used for rollback).</summary>
    Task ReplaceAllConfig(IReadOnlyList<RouteConfig> newRoutes, IReadOnlyList<ClusterConfig> newClusters,
        string source = "rollback", string? createdBy = "dashboard-user");

    /// <summary>Update heartbeat timestamp for a registered service.</summary>
    bool UpdateHeartbeat(string routeName, string? clientIp = null);

    /// <summary>Atomically rename a route (repoint references + delete old).</summary>
    Task<RouteOperationResult> TryRenameRoute(string oldRouteId, string newRouteId, RegisterRouteRequest request,
        string source = "dashboard", string? createdBy = "dashboard-user");

    /// <summary>Update circuit breaker configuration for a cluster.</summary>
    Task<bool> UpdateClusterCircuitBreakerAsync(string clusterId, Models.CircuitBreakerConfig? config);

    /// <summary>
    /// Reload static YARP config from <see cref="IConfiguration"/> (e.g. after appsettings.json hot-reload).
    /// Parses the <c>ReverseProxy</c> config section into native YARP route/cluster objects,
    /// merges with current dynamic overrides, and updates the in-memory provider.
    /// </summary>
    void ReloadStaticConfig(IConfiguration configuration);
}
