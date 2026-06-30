using Aneiang.Yarp.Dashboard.Infrastructure.Storage;
using Aneiang.Yarp.Dashboard.Modules.Policy.Models;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.Policy.Services;

/// <summary>
/// Service for managing route policies (retry + rate-limit + WAF toggle).
/// Extracted from <see cref="GatewayPolicyService"/> for single responsibility.
/// </summary>
public class RoutePolicyService : PolicyServiceBase
{
    public RoutePolicyService(
        IPolicyRepository policyRepo,
        IRouteRepository routeRepo,
        IClusterRepository clusterRepo,
        DynamicYarpConfigService yarpConfig,
        ILogger<RoutePolicyService> logger)
        : base(policyRepo, routeRepo, clusterRepo, yarpConfig, logger)
    {
    }

    public async Task<IReadOnlyList<RoutePolicy>> GetAllRoutePoliciesAsync()
    {
        var entities = await PolicyRepo.GetAllPoliciesAsync();
        var allTargets = await PolicyRepo.GetAllPolicyTargetsAsync();
        var policies = entities.ToRoutePolicies().ToList();

        foreach (var policy in policies)
        {
            policy.AppliedRoutes = allTargets
                .Where(t => string.Equals(t.PolicyId, policy.PolicyId, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(t.TargetType, "route", StringComparison.OrdinalIgnoreCase))
                .Select(t => t.TargetKeySnapshot)
                .ToList();
        }

        return policies.AsReadOnly();
    }

    public async Task<RoutePolicy?> GetRoutePolicyAsync(string policyId)
    {
        var entity = await PolicyRepo.GetPolicyAsync(policyId);
        if (entity?.PolicyType != "route") return null;
        var policy = entity.ToRoutePolicy();
        policy.AppliedRoutes = await GetAppliedTargetKeysAsync(policy.PolicyId, "route");
        return policy;
    }

    public async Task<RoutePolicy> CreateRoutePolicyAsync(RoutePolicy policy)
    {
        if (string.IsNullOrWhiteSpace(policy.PolicyId))
            policy.PolicyId = Guid.NewGuid().ToString("N")[..12];

        var existing = await PolicyRepo.GetPolicyAsync(policy.PolicyId);
        if (existing != null)
            throw new InvalidOperationException($"Policy with ID '{policy.PolicyId}' already exists");

        policy.CreatedAt = DateTime.UtcNow;
        await PolicyRepo.SavePolicyAsync(policy.ToEntity());

        Logger.LogInformation("Created route policy '{PolicyId}' ({Name})", policy.PolicyId, policy.DisplayName);
        return policy;
    }

    public async Task<RoutePolicy?> UpdateRoutePolicyAsync(string policyId, RoutePolicy policy)
    {
        var existing = await PolicyRepo.GetPolicyAsync(policyId);
        if (existing == null || existing.PolicyType != "route") return null;

        policy.PolicyId = policyId;
        policy.CreatedAt = existing.CreatedAt;
        policy.AppliedRoutes = await GetAppliedTargetKeysAsync(policyId, "route");

        await PolicyRepo.SavePolicyAsync(policy.ToEntity());
        Logger.LogDebug("Updated route policy '{PolicyId}'", policyId);

        // Re-apply policy metadata to all bound routes
        foreach (var routeId in policy.AppliedRoutes)
        {
            await ApplyRoutePolicyAsync(policyId, routeId);
        }

        return policy;
    }

    public async Task<bool> DeleteRoutePolicyAsync(string policyId)
    {
        var existing = await PolicyRepo.GetPolicyAsync(policyId);
        if (existing == null || existing.PolicyType != "route") return false;

        var appliedRoutes = await GetAppliedTargetKeysAsync(policyId, "route");
        foreach (var routeId in appliedRoutes)
        {
            await RemoveRoutePolicyMetadata(routeId);
        }

        await PolicyRepo.DeletePolicyTargetsAsync(policyId);
        await PolicyRepo.DeletePolicyAsync(policyId);
        Logger.LogInformation("Deleted route policy '{PolicyId}'", policyId);
        return true;
    }

    public async Task<bool> ApplyRoutePolicyAsync(string policyId, string routeId)
    {
        var policy = await GetRoutePolicyAsync(policyId);
        if (policy == null)
        {
            Logger.LogWarning("ApplyRoutePolicy: policy '{PolicyId}' not found", policyId);
            return false;
        }

        if (!policy.Enabled)
        {
            await RemoveRoutePolicyMetadata(routeId);
            Logger.LogDebug(
                "Route policy '{PolicyId}' is disabled, removed metadata from route '{RouteId}'",
                policyId, routeId);
            return true;
        }

        var metadata = policy.ToMetadata();
        var success = await YarpConfig.UpdateRouteMetadataAsync(routeId, metadata);

        if (success)
        {
            if (!policy.AppliedRoutes.Contains(routeId))
                policy.AppliedRoutes.Add(routeId);
            await SavePolicyTargetAsync(policy.PolicyId, "route", routeId);
            Logger.LogDebug(
                "Route policy '{PolicyId}' ({Name}) applied to route '{RouteId}'",
                policyId, policy.DisplayName, routeId);
        }

        return success;
    }

    public async Task<bool> UnapplyRoutePolicyAsync(string policyId, string routeId)
    {
        var policy = await GetRoutePolicyAsync(policyId);
        if (policy == null) return false;

        await RemoveRoutePolicyMetadata(routeId);

        if (policy.AppliedRoutes.Remove(routeId))
            await DeletePolicyTargetByKeyAsync(policy.PolicyId, "route", routeId);

        Logger.LogDebug("Route policy '{PolicyId}' unapplied from route '{RouteId}'", policyId, routeId);
        return true;
    }

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

        await YarpConfig.UpdateRouteMetadataAsync(routeId, metadataKeys);
    }
}
