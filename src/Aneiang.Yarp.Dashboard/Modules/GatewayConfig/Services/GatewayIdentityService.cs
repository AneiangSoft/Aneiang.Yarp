using System.Text.Json;
using Aneiang.Yarp.Dashboard.Infrastructure.State;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Storage.Entities;
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
    private readonly IPluginConfigurationRepository _pluginConfigurationRepository;
    private readonly IConfigPersistenceService _persistenceService;
    private readonly IDynamicYarpConfigService _dynamicConfig;
    private readonly ICircuitStateStore _circuitStore;
    private readonly ILogger<GatewayIdentityService> _logger;
    private readonly SemaphoreSlim _renameLock = new(1, 1);

    public GatewayIdentityService(
        IPluginConfigurationRepository pluginConfigurationRepository,
        IConfigPersistenceService persistenceService,
        IDynamicYarpConfigService dynamicConfig,
        ICircuitStateStore circuitStore,
        ILogger<GatewayIdentityService> logger)
    {
        _pluginConfigurationRepository = pluginConfigurationRepository;
        _persistenceService = persistenceService;
        _dynamicConfig = dynamicConfig;
        _circuitStore = circuitStore;
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
        var changed = await RewritePluginBindingsAsync(PluginBindingScope.Cluster, oldClusterId, newClusterId, ct);
        _circuitStore.RenameClusterKey(oldClusterId, newClusterId);
        _logger.LogInformation(
            "Cluster identity renamed: {OldClusterId} -> {NewClusterId}; updated {BindingCount} plugin binding(s)",
            oldClusterId, newClusterId, changed);
    }

    public async Task AfterRouteRenamedAsync(string oldRouteId, string newRouteId, CancellationToken ct = default)
    {
        var changed = await RewritePluginBindingsAsync(PluginBindingScope.Route, oldRouteId, newRouteId, ct);
        _logger.LogInformation(
            "Route identity renamed: {OldRouteId} -> {NewRouteId}; updated {BindingCount} plugin binding(s)",
            oldRouteId, newRouteId, changed);
    }

    private async Task<int> RewritePluginBindingsAsync(PluginBindingScope scope, string oldScopeId, string newScopeId, CancellationToken ct)
    {
        var bindings = await _pluginConfigurationRepository.GetBindingsAsync(scope, oldScopeId, ct);
        foreach (var binding in bindings)
        {
            binding.ScopeId = newScopeId;
            binding.ConfigVersion++;
            await _pluginConfigurationRepository.UpsertBindingAsync(binding, ct);
        }

        return bindings.Count;
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
}
