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
