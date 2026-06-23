using System.Text.Json;
using Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
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
    private readonly SemaphoreSlim _renameLock = new(1, 1);

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
        await _renameLock.WaitAsync(ct);
        ConfigSnapshot? rollbackSnapshot = null;
        try
        {
            rollbackSnapshot = await _persistenceService.SaveSnapshotAsync(
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

            if (!result.Success) return result;

            await AfterClusterRenamedAsync(oldClusterId, newClusterId, ct);
            return result;
        }
        catch (Exception ex)
        {
            await TryRollbackRenameAsync(rollbackSnapshot, clientIp, ct);
            _logger.LogError(ex, "Cluster rename failed and rollback was attempted: {OldClusterId} -> {NewClusterId}", oldClusterId, newClusterId);
            return new RouteOperationResult(false, $"Cluster rename failed: {ex.Message}");
        }
        finally
        {
            _renameLock.Release();
        }
    }

    public async Task<RouteOperationResult> RenameRouteAsync(
        string oldRouteId,
        string newRouteId,
        RegisterRouteRequest request,
        string? clientIp = null,
        string? operatorName = "dashboard-user",
        CancellationToken ct = default)
    {
        await _renameLock.WaitAsync(ct);
        ConfigSnapshot? rollbackSnapshot = null;
        try
        {
            rollbackSnapshot = await _persistenceService.SaveSnapshotAsync(
                $"Before route '{oldRouteId}' renamed to '{newRouteId}' via dashboard",
                clientIp);

            var result = await _dynamicConfig.TryRenameRoute(
                oldRouteId,
                newRouteId,
                request,
                source: "dashboard",
                createdBy: operatorName);

            if (!result.Success) return result;

            await AfterRouteRenamedAsync(oldRouteId, newRouteId, ct);
            return result;
        }
        catch (Exception ex)
        {
            await TryRollbackRenameAsync(rollbackSnapshot, clientIp, ct);
            _logger.LogError(ex, "Route rename failed and rollback was attempted: {OldRouteId} -> {NewRouteId}", oldRouteId, newRouteId);
            return new RouteOperationResult(false, $"Route rename failed: {ex.Message}");
        }
        finally
        {
            _renameLock.Release();
        }
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
            var changed = false;
            var targetRows = await _policyRepository.GetPolicyTargetsAsync(policy.PolicyId, policyType, ct);
            foreach (var targetRow in targetRows.Where(t => string.Equals(t.TargetKeySnapshot, oldTargetId, StringComparison.OrdinalIgnoreCase)))
            {
                targetRow.TargetKeySnapshot = newTargetId;
                await _policyRepository.SavePolicyTargetAsync(targetRow, ct);
                changed = true;
            }

            if (changed) changedCount++;
        }

        return changedCount;
    }

    private async Task TryRollbackRenameAsync(ConfigSnapshot? snapshot, string? clientIp, CancellationToken ct)
    {
        if (snapshot == null) return;
        try
        {
            await _persistenceService.RollbackAsync(snapshot.VersionId, clientIp);
        }
        catch (Exception rollbackEx) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(rollbackEx, "Failed to rollback rename using snapshot {VersionId}", snapshot.VersionId);
        }
    }

    private static string StableUidFromKey(string prefix, string key)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(prefix + ":" + key));
        return Convert.ToHexString(bytes, 0, 16).ToLowerInvariant();
    }
}
