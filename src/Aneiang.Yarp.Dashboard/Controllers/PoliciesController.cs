using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Dashboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Controllers;

/// <summary>
/// Gateway policy CRUD API.
/// </summary>
[Route("api/policies")]
[ApiController]
public class PoliciesController : ControllerBase
{
    private readonly IGatewayPolicyPersistenceService _persistence;
    private GatewayPolicyCollection _collection;

    public PoliciesController(IGatewayPolicyPersistenceService persistence)
    {
        _persistence = persistence;
        _collection = _persistence.Load();
    }

    /// <summary>Get all policies.</summary>
    [HttpGet]
    public IActionResult GetPolicies()
    {
        return Ok(new { code = 200, data = _collection });
    }

    /// <summary>Get a single policy by ID.</summary>
    [HttpGet("{policyId}")]
    public IActionResult GetPolicy(string policyId)
    {
        var policy = _collection.Policies.FirstOrDefault(p =>
            p.PolicyId.Equals(policyId, StringComparison.OrdinalIgnoreCase));
        if (policy == null)
            return NotFound(new { code = 404, message = $"Policy '{policyId}' not found" });
        return Ok(new { code = 200, data = policy });
    }

    /// <summary>Create a new policy.</summary>
    [HttpPost]
    public IActionResult CreatePolicy([FromBody] GatewayPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(policy.PolicyId))
            return BadRequest(new { code = 400, message = "PolicyId is required" });

        if (_collection.Policies.Any(p => p.PolicyId.Equals(policy.PolicyId, StringComparison.OrdinalIgnoreCase)))
            return BadRequest(new { code = 400, message = $"Policy '{policy.PolicyId}' already exists" });

        policy.CreatedAt = DateTime.UtcNow;
        _collection.Policies.Add(policy);
        _persistence.Save(_collection);
        return Ok(new { code = 200, data = policy });
    }

    /// <summary>Update an existing policy.</summary>
    [HttpPut("{policyId}")]
    public IActionResult UpdatePolicy(string policyId, [FromBody] GatewayPolicy policy)
    {
        var existing = _collection.Policies.FirstOrDefault(p =>
            p.PolicyId.Equals(policyId, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
            return NotFound(new { code = 404, message = $"Policy '{policyId}' not found" });

        existing.DisplayName = policy.DisplayName;
        existing.Description = policy.Description;
        existing.Priority = policy.Priority;
        existing.Enabled = policy.Enabled;
        existing.Tags = policy.Tags;
        existing.CircuitBreaker = policy.CircuitBreaker;
        existing.Retry = policy.Retry;
        existing.RateLimit = policy.RateLimit;
        existing.Waf = policy.Waf;
        existing.CustomPlugins = policy.CustomPlugins;

        _persistence.Save(_collection);
        return Ok(new { code = 200, data = existing });
    }

    /// <summary>Delete a policy.</summary>
    [HttpDelete("{policyId}")]
    public IActionResult DeletePolicy(string policyId)
    {
        var idx = _collection.Policies.FindIndex(p =>
            p.PolicyId.Equals(policyId, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
            return NotFound(new { code = 404, message = $"Policy '{policyId}' not found" });

        _collection.Policies.RemoveAt(idx);
        _persistence.Save(_collection);
        return Ok(new { code = 200, message = $"Policy '{policyId}' deleted" });
    }

    /// <summary>Toggle policy enabled state.</summary>
    [HttpPost("{policyId}/toggle")]
    public IActionResult TogglePolicy(string policyId)
    {
        var policy = _collection.Policies.FirstOrDefault(p =>
            p.PolicyId.Equals(policyId, StringComparison.OrdinalIgnoreCase));
        if (policy == null)
            return NotFound(new { code = 404, message = $"Policy '{policyId}' not found" });

        policy.Enabled = !policy.Enabled;
        _persistence.Save(_collection);
        return Ok(new { code = 200, data = new { policyId, enabled = policy.Enabled } });
    }
}
