using Aneiang.Yarp.Dashboard.Infrastructure.Common;
using Aneiang.Yarp.Dashboard.Infrastructure.Exceptions;
using Aneiang.Yarp.Dashboard.Modules.Policy.Application;
using Aneiang.Yarp.Dashboard.Modules.Policy.Models;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Modules.Policy.Controllers;

/// <summary>
/// Gateway policy API. Business logic delegated to <see cref="IPolicyAppService"/>.
/// </summary>
[Route("api/policies")]
[ApiController]
public class PoliciesController(IPolicyAppService appService) : ControllerBase
{
    [HttpGet("routes")]
    public async Task<IActionResult> GetRoutePolicies() => Ok(ApiResponse.Ok(await appService.GetRoutePoliciesAsync()));

    [HttpGet("routes/{policyId}")]
    public async Task<IActionResult> GetRoutePolicy(string policyId)
    {
        var policy = await appService.GetRoutePolicyAsync(policyId);
        if (policy == null) throw new NotFoundException("Route policy", policyId);
        return Ok(ApiResponse.Ok(policy));
    }

    [HttpPost("routes")]
    public async Task<IActionResult> CreateRoutePolicy([FromBody] RoutePolicy policy)
        => Ok(ApiResponse.Ok(await appService.CreateRoutePolicyAsync(policy)));

    [HttpPut("routes/{policyId}")]
    public async Task<IActionResult> UpdateRoutePolicy(string policyId, [FromBody] RoutePolicy policy)
    {
        var updated = await appService.UpdateRoutePolicyAsync(policyId, policy);
        if (updated == null) throw new NotFoundException("Route policy", policyId);
        return Ok(ApiResponse.Ok(updated));
    }

    [HttpDelete("routes/{policyId}")]
    public async Task<IActionResult> DeleteRoutePolicy(string policyId)
    {
        var deleted = await appService.DeleteRoutePolicyAsync(policyId);
        if (!deleted) throw new NotFoundException("Route policy", policyId);
        return Ok(ApiResponse.Ok($"Route policy '{policyId}' deleted"));
    }

    [HttpPost("routes/{policyId}/apply")]
    public async Task<IActionResult> ApplyRoutePolicy(string policyId, [FromBody] ApplyTargetRequest request)
    {
        var success = await appService.ApplyRoutePolicyAsync(policyId, request.TargetId);
        if (!success) return BadRequest(ApiResponse.Fail($"Failed to apply route policy '{policyId}' to route '{request.TargetId}'"));
        return Ok(ApiResponse.Ok($"Route policy '{policyId}' applied to route '{request.TargetId}'"));
    }

    [HttpDelete("routes/{policyId}/apply")]
    public async Task<IActionResult> UnapplyRoutePolicy(string policyId, [FromBody] ApplyTargetRequest request)
    {
        var success = await appService.UnapplyRoutePolicyAsync(policyId, request.TargetId);
        if (!success) return BadRequest(ApiResponse.Fail($"Failed to unapply route policy '{policyId}' from route '{request.TargetId}'"));
        return Ok(ApiResponse.Ok($"Route policy '{policyId}' unapplied from route '{request.TargetId}'"));
    }

    [HttpGet("clusters")]
    public async Task<IActionResult> GetClusterPolicies() => Ok(ApiResponse.Ok(await appService.GetClusterPoliciesAsync()));

    [HttpGet("clusters/{policyId}")]
    public async Task<IActionResult> GetClusterPolicy(string policyId)
    {
        var policy = await appService.GetClusterPolicyAsync(policyId);
        if (policy == null) throw new NotFoundException("Cluster policy", policyId);
        return Ok(ApiResponse.Ok(policy));
    }

    [HttpPost("clusters")]
    public async Task<IActionResult> CreateClusterPolicy([FromBody] ClusterPolicy policy)
        => Ok(ApiResponse.Ok(await appService.CreateClusterPolicyAsync(policy)));

    [HttpPut("clusters/{policyId}")]
    public async Task<IActionResult> UpdateClusterPolicy(string policyId, [FromBody] ClusterPolicy policy)
    {
        var updated = await appService.UpdateClusterPolicyAsync(policyId, policy);
        if (updated == null) throw new NotFoundException("Cluster policy", policyId);
        return Ok(ApiResponse.Ok(updated));
    }

    [HttpDelete("clusters/{policyId}")]
    public async Task<IActionResult> DeleteClusterPolicy(string policyId)
    {
        var deleted = await appService.DeleteClusterPolicyAsync(policyId);
        if (!deleted) throw new NotFoundException("Cluster policy", policyId);
        return Ok(ApiResponse.Ok($"Cluster policy '{policyId}' deleted"));
    }

    [HttpPost("clusters/{policyId}/apply")]
    public async Task<IActionResult> ApplyClusterPolicy(string policyId, [FromBody] ApplyTargetRequest request)
    {
        var success = await appService.ApplyClusterPolicyAsync(policyId, request.TargetId);
        if (!success) return BadRequest(ApiResponse.Fail($"Failed to apply cluster policy '{policyId}' to cluster '{request.TargetId}'"));
        return Ok(ApiResponse.Ok($"Cluster policy '{policyId}' applied to cluster '{request.TargetId}'"));
    }

    [HttpDelete("clusters/{policyId}/apply")]
    public async Task<IActionResult> UnapplyClusterPolicy(string policyId, [FromBody] ApplyTargetRequest request)
    {
        var success = await appService.UnapplyClusterPolicyAsync(policyId, request.TargetId);
        if (!success) return BadRequest(ApiResponse.Fail($"Failed to unapply cluster policy '{policyId}' from cluster '{request.TargetId}'"));
        return Ok(ApiResponse.Ok($"Cluster policy '{policyId}' unapplied from cluster '{request.TargetId}'"));
    }

    [HttpGet("routes/for-route/{routeId}")]
    public async Task<IActionResult> GetRoutePoliciesForRoute(string routeId)
        => Ok(ApiResponse.Ok(await appService.GetRoutePoliciesForRouteAsync(routeId)));

    [HttpGet("clusters/for-cluster/{clusterId}")]
    public async Task<IActionResult> GetClusterPoliciesForCluster(string clusterId)
        => Ok(ApiResponse.Ok(await appService.GetClusterPoliciesForClusterAsync(clusterId)));
}
