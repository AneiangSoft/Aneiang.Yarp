using Aneiang.Yarp.Dashboard.Modules.Policy.Models;
using Aneiang.Yarp.Dashboard.Modules.Policy.Services;

namespace Aneiang.Yarp.Dashboard.Modules.Policy.Application;

/// <summary>
/// Application service for gateway policy operations.
/// </summary>
public interface IPolicyAppService
{
    Task<object> GetRoutePoliciesAsync();
    Task<object?> GetRoutePolicyAsync(string policyId);
    Task<object> CreateRoutePolicyAsync(RoutePolicy policy);
    Task<object?> UpdateRoutePolicyAsync(string policyId, RoutePolicy policy);
    Task<bool> DeleteRoutePolicyAsync(string policyId);
    Task<bool> ApplyRoutePolicyAsync(string policyId, string routeId);
    Task<bool> UnapplyRoutePolicyAsync(string policyId, string routeId);
    Task<object> GetClusterPoliciesAsync();
    Task<object?> GetClusterPolicyAsync(string policyId);
    Task<object> CreateClusterPolicyAsync(ClusterPolicy policy);
    Task<object?> UpdateClusterPolicyAsync(string policyId, ClusterPolicy policy);
    Task<bool> DeleteClusterPolicyAsync(string policyId);
    Task<bool> ApplyClusterPolicyAsync(string policyId, string clusterId);
    Task<bool> UnapplyClusterPolicyAsync(string policyId, string clusterId);
    Task<object> GetRoutePoliciesForRouteAsync(string routeId);
    Task<object> GetClusterPoliciesForClusterAsync(string clusterId);
}

/// <inheritdoc/>
public class PolicyAppService : IPolicyAppService
{
    private readonly IGatewayPolicyService _policyService;

    public PolicyAppService(IGatewayPolicyService policyService)
    {
        _policyService = policyService;
    }

    public Task<object> GetRoutePoliciesAsync() => _policyService.GetAllRoutePoliciesAsync().ContinueWith(t => (object)t.Result);
    public async Task<object?> GetRoutePolicyAsync(string policyId) => await _policyService.GetRoutePolicyAsync(policyId);

    public async Task<object> CreateRoutePolicyAsync(RoutePolicy policy)
    {
        try { return await _policyService.CreateRoutePolicyAsync(policy); }
        catch (InvalidOperationException ex) { throw new Infrastructure.Exceptions.ConflictException(ex.Message); }
    }

    public async Task<object?> UpdateRoutePolicyAsync(string policyId, RoutePolicy policy)
        => await _policyService.UpdateRoutePolicyAsync(policyId, policy);

    public Task<bool> DeleteRoutePolicyAsync(string policyId) => _policyService.DeleteRoutePolicyAsync(policyId);
    public Task<bool> ApplyRoutePolicyAsync(string policyId, string routeId) => _policyService.ApplyRoutePolicyAsync(policyId, routeId);
    public Task<bool> UnapplyRoutePolicyAsync(string policyId, string routeId) => _policyService.UnapplyRoutePolicyAsync(policyId, routeId);

    public Task<object> GetClusterPoliciesAsync() => _policyService.GetAllClusterPoliciesAsync().ContinueWith(t => (object)t.Result);
    public async Task<object?> GetClusterPolicyAsync(string policyId) => await _policyService.GetClusterPolicyAsync(policyId);

    public async Task<object> CreateClusterPolicyAsync(ClusterPolicy policy)
    {
        try { return await _policyService.CreateClusterPolicyAsync(policy); }
        catch (InvalidOperationException ex) { throw new Infrastructure.Exceptions.ConflictException(ex.Message); }
    }

    public async Task<object?> UpdateClusterPolicyAsync(string policyId, ClusterPolicy policy)
        => await _policyService.UpdateClusterPolicyAsync(policyId, policy);

    public Task<bool> DeleteClusterPolicyAsync(string policyId) => _policyService.DeleteClusterPolicyAsync(policyId);
    public Task<bool> ApplyClusterPolicyAsync(string policyId, string clusterId) => _policyService.ApplyClusterPolicyAsync(policyId, clusterId);
    public Task<bool> UnapplyClusterPolicyAsync(string policyId, string clusterId) => _policyService.UnapplyClusterPolicyAsync(policyId, clusterId);

    public async Task<object> GetRoutePoliciesForRouteAsync(string routeId)
    {
        var all = await _policyService.GetAllRoutePoliciesAsync();
        return all.Where(p => p.AppliedRoutes.Contains(routeId)).ToList();
    }

    public async Task<object> GetClusterPoliciesForClusterAsync(string clusterId)
    {
        var all = await _policyService.GetAllClusterPoliciesAsync();
        return all.Where(p => p.AppliedClusters.Contains(clusterId)).ToList();
    }
}
