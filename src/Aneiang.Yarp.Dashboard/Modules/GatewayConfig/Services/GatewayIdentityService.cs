using System.Text.Json;
using Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;

public interface IGatewayIdentityService
{
    Task<RouteOperationResult> RenameClusterAsync(
        string oldClusterId,
        string newClusterId,
        Dictionary<string, string> destinations,
        string? loadBalancingPolicy = null,
        HealthCheckConfig? healthCheck = null,
        string? clientIp = null,
        string? operatorName = "dashboard-user",
        CancellationToken ct = default);

    Task<RouteOperationResult> RenameRouteAsync(
        string oldRouteId,
        string newRouteId,
        RegisterRouteRequest request,
        string? clientIp = null,
        string? operatorName = "dashboard-user",
        CancellationToken ct = default);

    Task AfterClusterRenamedAsync(string oldClusterId, string newClusterId, CancellationToken ct = default);
    Task AfterRouteRenamedAsync(string oldRouteId, string newRouteId, CancellationToken ct = default);
}

/// <summary>
/// Transitional identity service for key rename operations.
/// Long term this becomes the single entry point for UID/key rename operations.
/// </summary>
public sealed class GatewayIdentityService : IGatewayIdentityService
{
    private readonly IPolicyRepository _policyRepository;
    private readonly IConfigPersistenceService _persistenceService;
    private readonly IDynamicYarpConfigService _dynamicConfig;
    private readonly ILogger<GatewayIdentityService> _logger;

    public GatewayIdentityService(
        IPolicyRepository policyRepository,
        IConfigPersistenceService persistenceService,
        IDynamicYarpConfigService dynamicConfig,
        ILogger<GatewayIdentityService> logger)
    {
        _policyRepository = policyRepository;
        _persistenceService = persistenceService;
        _dynamicConfig = dynamicConfig;
        _logger = logger;
    }

    public async Task<RouteOperationResult> RenameClusterAsync(
        string oldClusterId,
        string newClusterId,
        Dictionary<string, string> destinations,
        string? loadBalancingPolicy = null,
        HealthCheckConfig? healthCheck = null,
        string? clientIp = null,
        string? operatorName = "dashboard-user",
        CancellationToken ct = default)
    {
        await _persistenceService.SaveSnapshotAsync(
            $"Before cluster '{oldClusterId}' renamed to '{newClusterId}' via dashboard",
            clientIp);

        var result = await _dynamicConfig.TryRenameCluster(
            oldClusterId,
            newClusterId,
            destinations,
            loadBalancingPolicy,
            healthCheck,
            source: "dashboard",
            createdBy: operatorName);

        if (result.Success)
            await AfterClusterRenamedAsync(oldClusterId, newClusterId, ct);

        return result;
    }

    public async Task<RouteOperationResult> RenameRouteAsync(
        string oldRouteId,
        string newRouteId,
        RegisterRouteRequest request,
        string? clientIp = null,
        string? operatorName = "dashboard-user",
        CancellationToken ct = default)
    {
        await _persistenceService.SaveSnapshotAsync(
            $"Before route '{oldRouteId}' renamed to '{newRouteId}' via dashboard",
            clientIp);

        var result = await _dynamicConfig.TryRenameRoute(
            oldRouteId,
            newRouteId,
            request,
            source: "dashboard",
            createdBy: operatorName);

        if (result.Success)
            await AfterRouteRenamedAsync(oldRouteId, newRouteId, ct);

        return result;
    }

    public async Task AfterClusterRenamedAsync(string oldClusterId, string newClusterId, CancellationToken ct = default)
    {
        var changed = await RewritePolicyTargetsAsync("cluster", oldClusterId, newClusterId, ct);
        CircuitBreakerMiddleware.RenameClusterKey(oldClusterId, newClusterId);
        _logger.LogInformation(
            "Cluster identity renamed: {OldClusterId} -> {NewClusterId}; updated {PolicyCount} policy binding(s)",
            oldClusterId, newClusterId, changed);
    }

    public async Task AfterRouteRenamedAsync(string oldRouteId, string newRouteId, CancellationToken ct = default)
    {
        var changed = await RewritePolicyTargetsAsync("route", oldRouteId, newRouteId, ct);
        _logger.LogInformation(
            "Route identity renamed: {OldRouteId} -> {NewRouteId}; updated {PolicyCount} policy binding(s)",
            oldRouteId, newRouteId, changed);
    }

    private async Task<int> RewritePolicyTargetsAsync(string policyType, string oldTargetId, string newTargetId, CancellationToken ct)
    {
        var changedCount = 0;
        var policies = await _policyRepository.GetAllPoliciesAsync(ct);
        foreach (var policy in policies.Where(p => string.Equals(p.PolicyType, policyType, StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(policy.AppliedTargets)) continue;

            List<string>? targets;
            try
            {
                targets = JsonSerializer.Deserialize<List<string>>(policy.AppliedTargets);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse applied targets for policy {PolicyId}", policy.PolicyId);
                continue;
            }

            if (targets == null) continue;
            var changed = false;
            for (var i = 0; i < targets.Count; i++)
            {
                if (string.Equals(targets[i], oldTargetId, StringComparison.OrdinalIgnoreCase))
                {
                    targets[i] = newTargetId;
                    changed = true;
                }
            }

            if (!changed) continue;
            policy.AppliedTargets = targets.Count > 0 ? JsonSerializer.Serialize(targets) : null;
            await _policyRepository.SavePolicyAsync(policy, ct);

            var targetRows = await _policyRepository.GetPolicyTargetsAsync(policy.PolicyId, policyType, ct);
            foreach (var targetRow in targetRows.Where(t => string.Equals(t.TargetKeySnapshot, oldTargetId, StringComparison.OrdinalIgnoreCase)))
            {
                targetRow.TargetKeySnapshot = newTargetId;
                await _policyRepository.SavePolicyTargetAsync(targetRow, ct);
            }

            changedCount++;
        }

        return changedCount;
    }
}
