using Aneiang.Yarp.Dashboard.Modules.Policy.Models;

namespace Aneiang.Yarp.Dashboard.Modules.Policy.Services;

/// <inheritdoc />
public class GatewayPolicyService : IGatewayPolicyService
{
    private readonly RoutePolicyService _routePolicyService;
    private readonly ClusterPolicyService _clusterPolicyService;

    /// <summary>Initializes a new instance of the <see cref="GatewayPolicyService"/> class.</summary>
    /// <param name="routePolicyService">Route policy service.</param>
    /// <param name="clusterPolicyService">Cluster policy service.</param>
    public GatewayPolicyService(
        RoutePolicyService routePolicyService,
        ClusterPolicyService clusterPolicyService)
    {
        _routePolicyService = routePolicyService;
        _clusterPolicyService = clusterPolicyService;
    }

    #region Route policy delegation

    /// <summary>Gets all route policies.</summary>
    public Task<IReadOnlyList<RoutePolicy>> GetAllRoutePoliciesAsync() =>
        _routePolicyService.GetAllRoutePoliciesAsync();

    /// <summary>Gets a route policy by its identifier.</summary>
    public Task<RoutePolicy?> GetRoutePolicyAsync(string policyId) =>
        _routePolicyService.GetRoutePolicyAsync(policyId);

    /// <summary>Creates a new route policy.</summary>
    public Task<RoutePolicy> CreateRoutePolicyAsync(RoutePolicy policy) =>
        _routePolicyService.CreateRoutePolicyAsync(policy);

    /// <summary>Updates an existing route policy.</summary>
    public Task<RoutePolicy?> UpdateRoutePolicyAsync(string policyId, RoutePolicy policy) =>
        _routePolicyService.UpdateRoutePolicyAsync(policyId, policy);

    /// <summary>Deletes a route policy by its identifier.</summary>
    public Task<bool> DeleteRoutePolicyAsync(string policyId) =>
        _routePolicyService.DeleteRoutePolicyAsync(policyId);

    /// <summary>Applies a route policy to a route.</summary>
    public Task<bool> ApplyRoutePolicyAsync(string policyId, string routeId) =>
        _routePolicyService.ApplyRoutePolicyAsync(policyId, routeId);

    /// <summary>Unapplies a route policy from a route.</summary>
    public Task<bool> UnapplyRoutePolicyAsync(string policyId, string routeId) =>
        _routePolicyService.UnapplyRoutePolicyAsync(policyId, routeId);

    #endregion

    #region Cluster policy delegation

    /// <summary>Gets all cluster policies.</summary>
    public Task<IReadOnlyList<ClusterPolicy>> GetAllClusterPoliciesAsync() =>
        _clusterPolicyService.GetAllClusterPoliciesAsync();

    /// <summary>Gets a cluster policy by its identifier.</summary>
    public Task<ClusterPolicy?> GetClusterPolicyAsync(string policyId) =>
        _clusterPolicyService.GetClusterPolicyAsync(policyId);

    /// <summary>Creates a new cluster policy.</summary>
    public Task<ClusterPolicy> CreateClusterPolicyAsync(ClusterPolicy policy) =>
        _clusterPolicyService.CreateClusterPolicyAsync(policy);

    /// <summary>Updates an existing cluster policy.</summary>
    public Task<ClusterPolicy?> UpdateClusterPolicyAsync(string policyId, ClusterPolicy policy) =>
        _clusterPolicyService.UpdateClusterPolicyAsync(policyId, policy);

    /// <summary>Deletes a cluster policy by its identifier.</summary>
    public Task<bool> DeleteClusterPolicyAsync(string policyId) =>
        _clusterPolicyService.DeleteClusterPolicyAsync(policyId);

    /// <summary>Applies a cluster policy to a cluster.</summary>
    public Task<bool> ApplyClusterPolicyAsync(string policyId, string clusterId) =>
        _clusterPolicyService.ApplyClusterPolicyAsync(policyId, clusterId);

    /// <summary>Unapplies a cluster policy from a cluster.</summary>
    public Task<bool> UnapplyClusterPolicyAsync(string policyId, string clusterId) =>
        _clusterPolicyService.UnapplyClusterPolicyAsync(policyId, clusterId);

    #endregion
}
