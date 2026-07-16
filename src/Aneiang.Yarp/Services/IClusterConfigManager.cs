using Aneiang.Yarp.Models;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

internal interface IClusterConfigManager
{
    Task<RouteOperationResult> TryAddClusterConfig(ClusterConfig cluster, string source, string? createdBy);

    Task<RouteOperationResult> TryAddCluster(string clusterId, Dictionary<string, string> destinations,
        string? loadBalancingPolicy, Models.HealthCheckConfig? healthCheck, string source, string? createdBy);

    Task<RouteOperationResult> TryAddCluster(CreateClusterRequest request, string source, string? createdBy);

    Task<RouteOperationResult> TryUpdateCluster(string clusterId, UpdateClusterRequest request);

    Task<RouteOperationResult> TryRemoveCluster(string clusterId);

    Task<RouteOperationResult> TryRenameCluster(string oldClusterId, string newClusterId,
        Dictionary<string, string> destinations, string? loadBalancingPolicy,
        Models.HealthCheckConfig? healthCheck, string source, string? createdBy);

    Task<bool> UpdateClusterCircuitBreakerAsync(string clusterId, CircuitBreakerConfig? config);

    IReadOnlyList<ClusterConfig> GetClusters();

    ClusterConfig? GetCluster(string clusterId);
}
