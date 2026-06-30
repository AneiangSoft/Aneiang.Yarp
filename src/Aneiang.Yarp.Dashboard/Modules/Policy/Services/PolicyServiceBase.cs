using Aneiang.Yarp.Dashboard.Infrastructure.Storage;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.Policy.Services;

/// <summary>
/// Shared base class for <see cref="RoutePolicyService"/> and <see cref="ClusterPolicyService"/>.
/// Provides repository access, target key resolution, and policy-target persistence helpers.
/// </summary>
public abstract class PolicyServiceBase
{
    protected readonly IPolicyRepository PolicyRepo;
    protected readonly IRouteRepository RouteRepo;
    protected readonly IClusterRepository ClusterRepo;
    protected readonly DynamicYarpConfigService YarpConfig;
    protected readonly ILogger Logger;

    protected PolicyServiceBase(
        IPolicyRepository policyRepo,
        IRouteRepository routeRepo,
        IClusterRepository clusterRepo,
        DynamicYarpConfigService yarpConfig,
        ILogger logger)
    {
        PolicyRepo = policyRepo;
        RouteRepo = routeRepo;
        ClusterRepo = clusterRepo;
        YarpConfig = yarpConfig;
        Logger = logger;
    }

    /// <summary>Get target keys applied to a policy.</summary>
    protected async Task<List<string>> GetAppliedTargetKeysAsync(string policyId, string targetType)
    {
        var targets = await PolicyRepo.GetPolicyTargetsAsync(policyId, targetType);
        return targets.Select(t => t.TargetKeySnapshot).ToList();
    }

    /// <summary>Persist a policy-to-target binding.</summary>
    protected async Task SavePolicyTargetAsync(string policyId, string targetType, string targetKey)
    {
        var policy = await PolicyRepo.GetPolicyAsync(policyId);
        if (policy == null) return;

        var targetUid = await ResolveTargetUidAsync(targetType, targetKey);
        await PolicyRepo.SavePolicyTargetAsync(new PolicyTargetEntity
        {
            PolicyUid = policy.PolicyUid,
            PolicyId = policy.PolicyId,
            TargetType = targetType,
            TargetUid = targetUid,
            TargetKeySnapshot = targetKey
        });
    }

    /// <summary>Delete a policy-to-target binding by key.</summary>
    protected async Task DeletePolicyTargetByKeyAsync(string policyId, string targetType, string targetKey)
    {
        var targetUid = await ResolveTargetUidAsync(targetType, targetKey);
        await PolicyRepo.DeletePolicyTargetAsync(policyId, targetType, targetUid);
    }

    private async Task<string> ResolveTargetUidAsync(string targetType, string targetKey)
    {
        if (string.Equals(targetType, "route", StringComparison.OrdinalIgnoreCase))
        {
            var route = await RouteRepo.GetRouteAsync(targetKey);
            if (!string.IsNullOrWhiteSpace(route?.RouteUid)) return route.RouteUid;
        }
        else if (string.Equals(targetType, "cluster", StringComparison.OrdinalIgnoreCase))
        {
            var cluster = await ClusterRepo.GetClusterAsync(targetKey);
            if (!string.IsNullOrWhiteSpace(cluster?.ClusterUid)) return cluster.ClusterUid;
        }

        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(targetType + ":" + targetKey));
        return Convert.ToHexString(bytes, 0, 16).ToLowerInvariant();
    }
}
