using Aneiang.Yarp.Dashboard.Infrastructure.State;
using Aneiang.Yarp.Dashboard.Infrastructure.Storage;
using Aneiang.Yarp.Dashboard.Modules.Policy.Models;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.Policy.Services;

/// <summary>
/// Service for managing cluster policies (circuit breaker).
/// Extracted from <see cref="GatewayPolicyService"/> for single responsibility.
/// </summary>
public class ClusterPolicyService : PolicyServiceBase
{
    private readonly ICircuitStateStore _circuitStore;

    public ClusterPolicyService(
        IPolicyRepository policyRepo,
        IRouteRepository routeRepo,
        IClusterRepository clusterRepo,
        DynamicYarpConfigService yarpConfig,
        ICircuitStateStore circuitStore,
        ILogger<ClusterPolicyService> logger)
        : base(policyRepo, routeRepo, clusterRepo, yarpConfig, logger)
    {
        _circuitStore = circuitStore;
    }

    public async Task<IReadOnlyList<ClusterPolicy>> GetAllClusterPoliciesAsync()
    {
        var entities = await PolicyRepo.GetAllPoliciesAsync();
        var allTargets = await PolicyRepo.GetAllPolicyTargetsAsync();
        var policies = entities.ToClusterPolicies().ToList();

        foreach (var policy in policies)
        {
            policy.AppliedClusters = allTargets
                .Where(t => string.Equals(t.PolicyId, policy.PolicyId, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(t.TargetType, "cluster", StringComparison.OrdinalIgnoreCase))
                .Select(t => t.TargetKeySnapshot)
                .ToList();
        }

        return policies.AsReadOnly();
    }

    public async Task<ClusterPolicy?> GetClusterPolicyAsync(string policyId)
    {
        var entity = await PolicyRepo.GetPolicyAsync(policyId);
        if (entity?.PolicyType != "cluster") return null;
        var policy = entity.ToClusterPolicy();
        policy.AppliedClusters = await GetAppliedTargetKeysAsync(policy.PolicyId, "cluster");
        return policy;
    }

    public async Task<ClusterPolicy> CreateClusterPolicyAsync(ClusterPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(policy.PolicyId))
            policy.PolicyId = Guid.NewGuid().ToString("N")[..12];

        var existing = await PolicyRepo.GetPolicyAsync(policy.PolicyId);
        if (existing != null)
            throw new InvalidOperationException($"Policy with ID '{policy.PolicyId}' already exists");

        policy.CreatedAt = DateTime.UtcNow;
        await PolicyRepo.SavePolicyAsync(policy.ToEntity());

        Logger.LogInformation("Created cluster policy '{PolicyId}' ({Name})", policy.PolicyId, policy.DisplayName);
        return policy;
    }

    public async Task<ClusterPolicy?> UpdateClusterPolicyAsync(string policyId, ClusterPolicy policy)
    {
        var existing = await PolicyRepo.GetPolicyAsync(policyId);
        if (existing == null || existing.PolicyType != "cluster") return null;

        policy.PolicyId = policyId;
        policy.CreatedAt = existing.CreatedAt;
        policy.AppliedClusters = await GetAppliedTargetKeysAsync(policyId, "cluster");

        await PolicyRepo.SavePolicyAsync(policy.ToEntity());
        Logger.LogInformation("Updated cluster policy '{PolicyId}'", policyId);

        foreach (var clusterId in policy.AppliedClusters)
            await ApplyClusterPolicyAsync(policyId, clusterId);

        return policy;
    }

    public async Task<bool> DeleteClusterPolicyAsync(string policyId)
    {
        var existing = await PolicyRepo.GetPolicyAsync(policyId);
        if (existing == null || existing.PolicyType != "cluster") return false;

        var appliedClusters = await GetAppliedTargetKeysAsync(policyId, "cluster");
        foreach (var clusterId in appliedClusters)
            await YarpConfig.UpdateClusterCircuitBreakerAsync(clusterId, null);

        await PolicyRepo.DeletePolicyTargetsAsync(policyId);
        await PolicyRepo.DeletePolicyAsync(policyId);
        Logger.LogInformation("Deleted cluster policy '{PolicyId}'", policyId);
        return true;
    }

    public async Task<bool> ApplyClusterPolicyAsync(string policyId, string clusterId)
    {
        var policy = await GetClusterPolicyAsync(policyId);
        if (policy == null)
        {
            Logger.LogWarning("ApplyClusterPolicy: policy '{PolicyId}' not found", policyId);
            return false;
        }

        if (!policy.Enabled)
        {
            await YarpConfig.UpdateClusterCircuitBreakerAsync(clusterId, null);
            Logger.LogInformation(
                "Cluster policy '{PolicyId}' is disabled, cleared circuit breaker from cluster '{ClusterId}'",
                policyId, clusterId);
            return true;
        }

        var circuitBreakerConfig = policy.ToCircuitBreakerConfig();
        var success = await YarpConfig.UpdateClusterCircuitBreakerAsync(clusterId, circuitBreakerConfig);

        if (success)
        {
            if (!policy.AppliedClusters.Contains(clusterId))
                policy.AppliedClusters.Add(clusterId);
            await SavePolicyTargetAsync(policy.PolicyId, "cluster", clusterId);
            Logger.LogInformation(
                "Cluster policy '{PolicyId}' ({Name}) applied to cluster '{ClusterId}'",
                policyId, policy.DisplayName, clusterId);
        }

        return success;
    }

    public async Task<bool> UnapplyClusterPolicyAsync(string policyId, string clusterId)
    {
        var policy = await GetClusterPolicyAsync(policyId);
        if (policy == null) return false;

        await YarpConfig.UpdateClusterCircuitBreakerAsync(clusterId, null);

        var dynConfig = YarpConfig.GetDynamicConfig();
        var cluster = dynConfig?.Clusters.FirstOrDefault(c =>
            string.Equals(c.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
        _circuitStore.RemoveForCluster(clusterId, cluster?.ClusterUid);

        if (policy.AppliedClusters.Remove(clusterId))
            await DeletePolicyTargetByKeyAsync(policy.PolicyId, "cluster", clusterId);

        Logger.LogInformation(
            "Cluster policy '{PolicyId}' unapplied from cluster '{ClusterId}'",
            policyId, clusterId);
        return true;
    }
}
