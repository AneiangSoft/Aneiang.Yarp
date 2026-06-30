using Aneiang.Yarp.Dashboard.Modules.Policy.Models;

namespace Aneiang.Yarp.Dashboard.Modules.Policy.Services;

/// <summary>
/// Gateway policy service facade — delegates to <see cref="RoutePolicyService"/>
/// and <see cref="ClusterPolicyService"/>.
/// Kept for backward compatibility with existing consumers.
/// </summary>
public interface IGatewayPolicyService
{
    Task<IReadOnlyList<RoutePolicy>> GetAllRoutePoliciesAsync();
    Task<RoutePolicy?> GetRoutePolicyAsync(string policyId);
    Task<RoutePolicy> CreateRoutePolicyAsync(RoutePolicy policy);
    Task<RoutePolicy?> UpdateRoutePolicyAsync(string policyId, RoutePolicy policy);
    Task<bool> DeleteRoutePolicyAsync(string policyId);
    Task<bool> ApplyRoutePolicyAsync(string policyId, string routeId);
    Task<bool> UnapplyRoutePolicyAsync(string policyId, string routeId);

    Task<IReadOnlyList<ClusterPolicy>> GetAllClusterPoliciesAsync();
    Task<ClusterPolicy?> GetClusterPolicyAsync(string policyId);
    Task<ClusterPolicy> CreateClusterPolicyAsync(ClusterPolicy policy);
    Task<ClusterPolicy?> UpdateClusterPolicyAsync(string policyId, ClusterPolicy policy);
    Task<bool> DeleteClusterPolicyAsync(string policyId);
    Task<bool> ApplyClusterPolicyAsync(string policyId, string clusterId);
    Task<bool> UnapplyClusterPolicyAsync(string policyId, string clusterId);
}

/// <inheritdoc />
public class GatewayPolicyService : IGatewayPolicyService
{
    private readonly RoutePolicyService _routePolicyService;
    private readonly ClusterPolicyService _clusterPolicyService;

    public GatewayPolicyService(
        RoutePolicyService routePolicyService,
        ClusterPolicyService clusterPolicyService)
    {
        _routePolicyService = routePolicyService;
        _clusterPolicyService = clusterPolicyService;
    }

    // ── Route policy delegation ──────────────────────────────────────────

    public Task<IReadOnlyList<RoutePolicy>> GetAllRoutePoliciesAsync() =>
        _routePolicyService.GetAllRoutePoliciesAsync();

    public Task<RoutePolicy?> GetRoutePolicyAsync(string policyId) =>
        _routePolicyService.GetRoutePolicyAsync(policyId);

    public Task<RoutePolicy> CreateRoutePolicyAsync(RoutePolicy policy) =>
        _routePolicyService.CreateRoutePolicyAsync(policy);

    public Task<RoutePolicy?> UpdateRoutePolicyAsync(string policyId, RoutePolicy policy) =>
        _routePolicyService.UpdateRoutePolicyAsync(policyId, policy);

    public Task<bool> DeleteRoutePolicyAsync(string policyId) =>
        _routePolicyService.DeleteRoutePolicyAsync(policyId);

    public Task<bool> ApplyRoutePolicyAsync(string policyId, string routeId) =>
        _routePolicyService.ApplyRoutePolicyAsync(policyId, routeId);

    public Task<bool> UnapplyRoutePolicyAsync(string policyId, string routeId) =>
        _routePolicyService.UnapplyRoutePolicyAsync(policyId, routeId);

    // ── Cluster policy delegation ────────────────────────────────────────

    public Task<IReadOnlyList<ClusterPolicy>> GetAllClusterPoliciesAsync() =>
        _clusterPolicyService.GetAllClusterPoliciesAsync();

    public Task<ClusterPolicy?> GetClusterPolicyAsync(string policyId) =>
        _clusterPolicyService.GetClusterPolicyAsync(policyId);

    public Task<ClusterPolicy> CreateClusterPolicyAsync(ClusterPolicy policy) =>
        _clusterPolicyService.CreateClusterPolicyAsync(policy);

    public Task<ClusterPolicy?> UpdateClusterPolicyAsync(string policyId, ClusterPolicy policy) =>
        _clusterPolicyService.UpdateClusterPolicyAsync(policyId, policy);

    public Task<bool> DeleteClusterPolicyAsync(string policyId) =>
        _clusterPolicyService.DeleteClusterPolicyAsync(policyId);

    public Task<bool> ApplyClusterPolicyAsync(string policyId, string clusterId) =>
        _clusterPolicyService.ApplyClusterPolicyAsync(policyId, clusterId);

    public Task<bool> UnapplyClusterPolicyAsync(string policyId, string clusterId) =>
        _clusterPolicyService.UnapplyClusterPolicyAsync(policyId, clusterId);
}
