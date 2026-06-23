using Aneiang.Yarp.Dashboard.Modules.Policy.Models;
using Aneiang.Yarp.Dashboard.Modules.Policy.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Modules.Policy.Controllers;

/// <summary>
/// Gateway policy API: route policies and cluster policies with apply/unapply.
/// </summary>
[Route("api/policies")]
[ApiController]
public class PoliciesController : ControllerBase
{
    private readonly IGatewayPolicyService _policyService;

    public PoliciesController(IGatewayPolicyService policyService)
    {
        _policyService = policyService;
    }

    /// <summary>Get all route policies.</summary>
    [HttpGet("routes")]
    public async Task<IActionResult> GetRoutePolicies()
    {
        var policies = await _policyService.GetAllRoutePoliciesAsync();
        return Ok(new { code = 200, data = policies });
    }

    /// <summary>Get a single route policy by ID.</summary>
    [HttpGet("routes/{policyId}")]
    public async Task<IActionResult> GetRoutePolicy(string policyId)
    {
        var policy = await _policyService.GetRoutePolicyAsync(policyId);
        if (policy == null)
            return NotFound(new { code = 404, message = $"Route policy '{policyId}' not found" });
        return Ok(new { code = 200, data = policy });
    }

    /// <summary>Create a new route policy.</summary>
    [HttpPost("routes")]
    public async Task<IActionResult> CreateRoutePolicy([FromBody] RoutePolicy policy)
    {
        try
        {
            var created = await _policyService.CreateRoutePolicyAsync(policy);
            return Ok(new { code = 200, data = created });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>Update an existing route policy.</summary>
    [HttpPut("routes/{policyId}")]
    public async Task<IActionResult> UpdateRoutePolicy(string policyId, [FromBody] RoutePolicy policy)
    {
        var updated = await _policyService.UpdateRoutePolicyAsync(policyId, policy);
        if (updated == null)
            return NotFound(new { code = 404, message = $"Route policy '{policyId}' not found" });
        return Ok(new { code = 200, data = updated });
    }

    /// <summary>Delete a route policy and unapply from all routes.</summary>
    [HttpDelete("routes/{policyId}")]
    public async Task<IActionResult> DeleteRoutePolicy(string policyId)
    {
        var deleted = await _policyService.DeleteRoutePolicyAsync(policyId);
        if (!deleted)
            return NotFound(new { code = 404, message = $"Route policy '{policyId}' not found" });
        return Ok(new { code = 200, message = $"Route policy '{policyId}' deleted" });
    }

    /// <summary>Apply a route policy to a route.</summary>
    [HttpPost("routes/{policyId}/apply")]
    public async Task<IActionResult> ApplyRoutePolicy(string policyId, [FromBody] ApplyTargetRequest request)
    {
        var success = await _policyService.ApplyRoutePolicyAsync(policyId, request.TargetId);
        if (!success)
            return BadRequest(new { code = 400, message = $"Failed to apply route policy '{policyId}' to route '{request.TargetId}'" });
        return Ok(new { code = 200, message = $"Route policy '{policyId}' applied to route '{request.TargetId}'" });
    }

    /// <summary>Unapply a route policy from a route.</summary>
    [HttpDelete("routes/{policyId}/apply")]
    public async Task<IActionResult> UnapplyRoutePolicy(string policyId, [FromBody] ApplyTargetRequest request)
    {
        var success = await _policyService.UnapplyRoutePolicyAsync(policyId, request.TargetId);
        if (!success)
            return BadRequest(new { code = 400, message = $"Failed to unapply route policy '{policyId}' from route '{request.TargetId}'" });
        return Ok(new { code = 200, message = $"Route policy '{policyId}' unapplied from route '{request.TargetId}'" });
    }

    /// <summary>Get all cluster policies.</summary>
    [HttpGet("clusters")]
    public async Task<IActionResult> GetClusterPolicies()
    {
        var policies = await _policyService.GetAllClusterPoliciesAsync();
        return Ok(new { code = 200, data = policies });
    }

    /// <summary>Get a single cluster policy by ID.</summary>
    [HttpGet("clusters/{policyId}")]
    public async Task<IActionResult> GetClusterPolicy(string policyId)
    {
        var policy = await _policyService.GetClusterPolicyAsync(policyId);
        if (policy == null)
            return NotFound(new { code = 404, message = $"Cluster policy '{policyId}' not found" });
        return Ok(new { code = 200, data = policy });
    }

    /// <summary>Create a new cluster policy.</summary>
    [HttpPost("clusters")]
    public async Task<IActionResult> CreateClusterPolicy([FromBody] ClusterPolicy policy)
    {
        try
        {
            var created = await _policyService.CreateClusterPolicyAsync(policy);
            return Ok(new { code = 200, data = created });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>Update an existing cluster policy.</summary>
    [HttpPut("clusters/{policyId}")]
    public async Task<IActionResult> UpdateClusterPolicy(string policyId, [FromBody] ClusterPolicy policy)
    {
        var updated = await _policyService.UpdateClusterPolicyAsync(policyId, policy);
        if (updated == null)
            return NotFound(new { code = 404, message = $"Cluster policy '{policyId}' not found" });
        return Ok(new { code = 200, data = updated });
    }

    /// <summary>Delete a cluster policy and unapply from all clusters.</summary>
    [HttpDelete("clusters/{policyId}")]
    public async Task<IActionResult> DeleteClusterPolicy(string policyId)
    {
        var deleted = await _policyService.DeleteClusterPolicyAsync(policyId);
        if (!deleted)
            return NotFound(new { code = 404, message = $"Cluster policy '{policyId}' not found" });
        return Ok(new { code = 200, message = $"Cluster policy '{policyId}' deleted" });
    }

    /// <summary>Apply a cluster policy to a cluster.</summary>
    [HttpPost("clusters/{policyId}/apply")]
    public async Task<IActionResult> ApplyClusterPolicy(string policyId, [FromBody] ApplyTargetRequest request)
    {
        var success = await _policyService.ApplyClusterPolicyAsync(policyId, request.TargetId);
        if (!success)
            return BadRequest(new { code = 400, message = $"Failed to apply cluster policy '{policyId}' to cluster '{request.TargetId}'" });
        return Ok(new { code = 200, message = $"Cluster policy '{policyId}' applied to cluster '{request.TargetId}'" });
    }

    /// <summary>Unapply a cluster policy from a cluster.</summary>
    [HttpDelete("clusters/{policyId}/apply")]
    public async Task<IActionResult> UnapplyClusterPolicy(string policyId, [FromBody] ApplyTargetRequest request)
    {
        var success = await _policyService.UnapplyClusterPolicyAsync(policyId, request.TargetId);
        if (!success)
            return BadRequest(new { code = 400, message = $"Failed to unapply cluster policy '{policyId}' from cluster '{request.TargetId}'" });
        return Ok(new { code = 200, message = $"Cluster policy '{policyId}' unapplied from cluster '{request.TargetId}'" });
    }

    /// <summary>Find route policies applied to a specific route.</summary>
    [HttpGet("routes/for-route/{routeId}")]
    public async Task<IActionResult> GetRoutePoliciesForRoute(string routeId)
    {
        var allPolicies = await _policyService.GetAllRoutePoliciesAsync();
        var applied = allPolicies.Where(p => p.AppliedRoutes.Contains(routeId)).ToList();
        return Ok(new { code = 200, data = applied });
    }

    /// <summary>Find cluster policies applied to a specific cluster.</summary>
    [HttpGet("clusters/for-cluster/{clusterId}")]
    public async Task<IActionResult> GetClusterPoliciesForCluster(string clusterId)
    {
        var allPolicies = await _policyService.GetAllClusterPoliciesAsync();
        var applied = allPolicies.Where(p => p.AppliedClusters.Contains(clusterId)).ToList();
        return Ok(new { code = 200, data = applied });
    }
}

/// <summary>Request body for apply/unapply operations.</summary>
public class ApplyTargetRequest
{
    /// <summary>Route ID or Cluster ID to apply/unapply the policy to.</summary>
    public string TargetId { get; set; } = string.Empty;
}
