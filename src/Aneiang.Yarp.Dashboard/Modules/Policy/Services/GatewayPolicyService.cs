using Aneiang.Yarp.Dashboard.Infrastructure.Storage;
using Aneiang.Yarp.Dashboard.Modules.Policy.Models;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.Policy.Services;

/// <summary>
/// Service for managing gateway policies using gateway repository.
/// Supports route policies (retry + rate-limit + WAF toggle) and cluster policies (circuit breaker).
/// </summary>
public interface IGatewayPolicyService
{
    // ─── Route Policies ─────────────────────────────
    Task<IReadOnlyList<RoutePolicy>> GetAllRoutePoliciesAsync();
    Task<RoutePolicy?> GetRoutePolicyAsync(string policyId);
    Task<RoutePolicy> CreateRoutePolicyAsync(RoutePolicy policy);
    Task<RoutePolicy?> UpdateRoutePolicyAsync(string policyId, RoutePolicy policy);
    Task<bool> DeleteRoutePolicyAsync(string policyId);
    Task<bool> ApplyRoutePolicyAsync(string policyId, string routeId);
    Task<bool> UnapplyRoutePolicyAsync(string policyId, string routeId);

    // ─── Cluster Policies ───────────────────────────
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
    private readonly IGatewayRepository _repository;
    private readonly DynamicYarpConfigService _yarpConfig;
    private readonly ILogger<GatewayPolicyService> _logger;

    public GatewayPolicyService(
        IGatewayRepository repository,
        DynamicYarpConfigService yarpConfig,
        ILogger<GatewayPolicyService> logger)
    {
        _repository = repository;
        _yarpConfig = yarpConfig;
        _logger = logger;
    }

    // ─── Route Policies ─────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoutePolicy>> GetAllRoutePoliciesAsync()
    {
        var entities = await _repository.GetAllPoliciesAsync();
        return entities.ToRoutePolicies().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<RoutePolicy?> GetRoutePolicyAsync(string policyId)
    {
        var entity = await _repository.GetPolicyAsync(policyId);
        return entity?.PolicyType == "route" ? entity.ToRoutePolicy() : null;
    }

    /// <inheritdoc />
    public async Task<RoutePolicy> CreateRoutePolicyAsync(RoutePolicy policy)
    {
        if (string.IsNullOrWhiteSpace(policy.PolicyId))
            policy.PolicyId = Guid.NewGuid().ToString("N")[..12];

        var existing = await _repository.GetPolicyAsync(policy.PolicyId);
        if (existing != null)
            throw new InvalidOperationException($"Policy with ID '{policy.PolicyId}' already exists");

        policy.CreatedAt = DateTime.UtcNow;
        await _repository.SavePolicyAsync(policy.ToEntity());

        _logger.LogInformation("Created route policy '{PolicyId}' ({Name})", policy.PolicyId, policy.DisplayName);
        return policy;
    }

    /// <inheritdoc />
    public async Task<RoutePolicy?> UpdateRoutePolicyAsync(string policyId, RoutePolicy policy)
    {
        var existing = await _repository.GetPolicyAsync(policyId);
        if (existing == null || existing.PolicyType != "route") return null;

        policy.PolicyId = policyId;
        policy.CreatedAt = existing.CreatedAt;
        policy.AppliedRoutes = existing.AppliedTargets != null
            ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(existing.AppliedTargets) ?? new()
            : new();

        await _repository.SavePolicyAsync(policy.ToEntity());
        _logger.LogInformation("Updated route policy '{PolicyId}'", policyId);

        // Re-apply policy metadata to all bound routes
        foreach (var routeId in policy.AppliedRoutes)
        {
            await ApplyRoutePolicyAsync(policyId, routeId);
        }

        return policy;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteRoutePolicyAsync(string policyId)
    {
        var existing = await _repository.GetPolicyAsync(policyId);
        if (existing == null || existing.PolicyType != "route") return false;

        var policy = existing.ToRoutePolicy();
        foreach (var routeId in policy.AppliedRoutes)
        {
            await RemoveRoutePolicyMetadata(routeId);
        }

        await _repository.DeletePolicyAsync(policyId);
        _logger.LogInformation("Deleted route policy '{PolicyId}'", policyId);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ApplyRoutePolicyAsync(string policyId, string routeId)
    {
        var policy = await GetRoutePolicyAsync(policyId);
        if (policy == null)
        {
            _logger.LogWarning("ApplyRoutePolicy: policy '{PolicyId}' not found", policyId);
            return false;
        }

        if (!policy.Enabled)
        {
            // When policy is disabled, remove its metadata from the route instead of skip
            await RemoveRoutePolicyMetadata(routeId);
            _logger.LogInformation(
                "Route policy '{PolicyId}' is disabled, removed metadata from route '{RouteId}'",
                policyId, routeId);
            return true;
        }

        var metadata = policy.ToMetadata();
        var success = await _yarpConfig.UpdateRouteMetadataAsync(routeId, metadata);

        if (success)
        {
            if (!policy.AppliedRoutes.Contains(routeId))
            {
                policy.AppliedRoutes.Add(routeId);
                await _repository.SavePolicyAsync(policy.ToEntity());
            }
            _logger.LogInformation(
                "Route policy '{PolicyId}' ({Name}) applied to route '{RouteId}'",
                policyId, policy.DisplayName, routeId);
        }

        return success;
    }

    /// <inheritdoc />
    public async Task<bool> UnapplyRoutePolicyAsync(string policyId, string routeId)
    {
        var policy = await GetRoutePolicyAsync(policyId);
        if (policy == null) return false;

        await RemoveRoutePolicyMetadata(routeId);

        if (policy.AppliedRoutes.Remove(routeId))
        {
            await _repository.SavePolicyAsync(policy.ToEntity());
        }

        _logger.LogInformation(
            "Route policy '{PolicyId}' unapplied from route '{RouteId}'",
            policyId, routeId);
        return true;
    }

    // ─── Cluster Policies ───────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClusterPolicy>> GetAllClusterPoliciesAsync()
    {
        var entities = await _repository.GetAllPoliciesAsync();
        return entities.ToClusterPolicies().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<ClusterPolicy?> GetClusterPolicyAsync(string policyId)
    {
        var entity = await _repository.GetPolicyAsync(policyId);
        return entity?.PolicyType == "cluster" ? entity.ToClusterPolicy() : null;
    }

    /// <inheritdoc />
    public async Task<ClusterPolicy> CreateClusterPolicyAsync(ClusterPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(policy.PolicyId))
            policy.PolicyId = Guid.NewGuid().ToString("N")[..12];

        var existing = await _repository.GetPolicyAsync(policy.PolicyId);
        if (existing != null)
            throw new InvalidOperationException($"Policy with ID '{policy.PolicyId}' already exists");

        policy.CreatedAt = DateTime.UtcNow;
        await _repository.SavePolicyAsync(policy.ToEntity());

        _logger.LogInformation("Created cluster policy '{PolicyId}' ({Name})", policy.PolicyId, policy.DisplayName);
        return policy;
    }

    /// <inheritdoc />
    public async Task<ClusterPolicy?> UpdateClusterPolicyAsync(string policyId, ClusterPolicy policy)
    {
        var existing = await _repository.GetPolicyAsync(policyId);
        if (existing == null || existing.PolicyType != "cluster") return null;

        policy.PolicyId = policyId;
        policy.CreatedAt = existing.CreatedAt;
        policy.AppliedClusters = existing.AppliedTargets != null
            ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(existing.AppliedTargets) ?? new()
            : new();

        await _repository.SavePolicyAsync(policy.ToEntity());
        _logger.LogInformation("Updated cluster policy '{PolicyId}'", policyId);

        // Re-apply policy to all bound clusters
        foreach (var clusterId in policy.AppliedClusters)
        {
            await ApplyClusterPolicyAsync(policyId, clusterId);
        }

        return policy;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteClusterPolicyAsync(string policyId)
    {
        var existing = await _repository.GetPolicyAsync(policyId);
        if (existing == null || existing.PolicyType != "cluster") return false;

        var policy = existing.ToClusterPolicy();
        foreach (var clusterId in policy.AppliedClusters)
        {
            await _yarpConfig.UpdateClusterCircuitBreakerAsync(clusterId, null);
        }

        await _repository.DeletePolicyAsync(policyId);
        _logger.LogInformation("Deleted cluster policy '{PolicyId}'", policyId);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ApplyClusterPolicyAsync(string policyId, string clusterId)
    {
        var policy = await GetClusterPolicyAsync(policyId);
        if (policy == null)
        {
            _logger.LogWarning("ApplyClusterPolicy: policy '{PolicyId}' not found", policyId);
            return false;
        }

        if (!policy.Enabled)
        {
            // When policy is disabled, clear circuit breaker config from cluster
            await _yarpConfig.UpdateClusterCircuitBreakerAsync(clusterId, null);
            _logger.LogInformation(
                "Cluster policy '{PolicyId}' is disabled, cleared circuit breaker from cluster '{ClusterId}'",
                policyId, clusterId);
            return true;
        }

        var circuitBreakerConfig = policy.ToCircuitBreakerConfig();
        var success = await _yarpConfig.UpdateClusterCircuitBreakerAsync(clusterId, circuitBreakerConfig);

        if (success)
        {
            if (!policy.AppliedClusters.Contains(clusterId))
            {
                policy.AppliedClusters.Add(clusterId);
                await _repository.SavePolicyAsync(policy.ToEntity());
            }
            _logger.LogInformation(
                "Cluster policy '{PolicyId}' ({Name}) applied to cluster '{ClusterId}'",
                policyId, policy.DisplayName, clusterId);
        }

        return success;
    }

    /// <inheritdoc />
    public async Task<bool> UnapplyClusterPolicyAsync(string policyId, string clusterId)
    {
        var policy = await GetClusterPolicyAsync(policyId);
        if (policy == null) return false;

        await _yarpConfig.UpdateClusterCircuitBreakerAsync(clusterId, null);

        if (policy.AppliedClusters.Remove(clusterId))
        {
            await _repository.SavePolicyAsync(policy.ToEntity());
        }

        _logger.LogInformation(
            "Cluster policy '{PolicyId}' unapplied from cluster '{ClusterId}'",
            policyId, clusterId);
        return true;
    }

    // ─── Helpers ────────────────────────────────────

    private async Task RemoveRoutePolicyMetadata(string routeId)
    {
        var metadataKeys = new Dictionary<string, string>();
        string[] policyKeys = [
            "Retry:Enabled", "Retry:MaxRetries", "Retry:BackoffBaseMs", "Retry:BackoffJitterMs",
            "Retry:TimeoutSeconds", "Retry:UseDifferentDestination", "Retry:RetryNonIdempotent",
            "Retry:RetryOnStatusCodes", "RateLimit:Enabled", "RateLimit:Algorithm",
            "RateLimit:PermitLimit", "RateLimit:Window", "RateLimit:QueueLimit",
            "RateLimit:PartitionKey", "Waf:Enabled", "Policy:Id", "Policy:Name"
        ];

        foreach (var key in policyKeys)
            metadataKeys[key] = string.Empty;

        await _yarpConfig.UpdateRouteMetadataAsync(routeId, metadataKeys);
    }
}
