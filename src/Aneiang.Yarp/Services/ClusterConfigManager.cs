using Aneiang.Yarp.Models;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>
/// All cluster CRUD operations on the dynamic config working set.
/// <see cref="DynamicConfigState"/> is the single authoritative data source.
/// </summary>
internal class ClusterConfigManager : ConfigManagerBase, IClusterConfigManager
{
    private readonly AneiangProxyConfigProvider _configProvider;
    private readonly ILogger<ClusterConfigManager> _logger;

    public ClusterConfigManager(
        AneiangProxyConfigProvider configProvider,
        DynamicConfigState state,
        SemaphoreSlim semaphore,
        IDynamicConfigPersister persister,
        IDynamicConfigPublisher publisher,
        IConfigChangeAuditLog auditLog,
        ILogger<ClusterConfigManager> logger)
        : base(state, semaphore, persister, publisher, auditLog)
    {
        _configProvider = configProvider;
        _logger = logger;
    }

    protected override void LogPersistError(Exception ex, string operationName, string? targetName)
        => _logger.LogError(ex, "Persist failed for {Operation} on {Target}", operationName, targetName);

    #region TryAddClusterConfig (full native config)

    public async Task<RouteOperationResult> TryAddClusterConfig(
        ClusterConfig cluster, string source, string? createdBy)
    {
        if (string.IsNullOrWhiteSpace(cluster.ClusterId))
            return new RouteOperationResult(false, "Cluster ID cannot be empty");
        if (cluster.Destinations == null || cluster.Destinations.Count == 0)
        {
            AuditLog.RecordFailure("AddCluster", cluster.ClusterId, "At least one destination is required");
            return new RouteOperationResult(false, "At least one destination is required");
        }

        return await ExecuteWithLockAsync(
            "AddCluster", cluster.ClusterId, async config =>
        {
            var dc = config.Clusters.FirstOrDefault(c =>
                string.Equals(c.Config.ClusterId, cluster.ClusterId, StringComparison.OrdinalIgnoreCase));

            bool isNew;
            if (dc == null)
            {
                isNew = true;
                config.Clusters.Add(new DynamicClusterConfig
                { Config = cluster, Source = source, CreatedAt = DateTime.UtcNow, CreatedBy = createdBy });
            }
            else
            {
                isNew = false;
                dc.Config = cluster;
                if (!string.IsNullOrEmpty(source) && source != dc.Source)
                { dc.Source = source; dc.CreatedBy = createdBy; }
            }

            var action = isNew ? "created" : "updated";
            _logger.LogInformation("Cluster '{ClusterId}' {Action} via full config with {DestCount} destinations",
                cluster.ClusterId, action, cluster.Destinations.Count);
            AuditLog.RecordSuccess(isNew ? "AddCluster" : "UpdateCluster", cluster.ClusterId, createdBy, null, null,
                new { destinations = cluster.Destinations.ToDictionary(d => d.Key, d => d.Value.Address), loadBalancingPolicy = cluster.LoadBalancingPolicy });
            return new RouteOperationResult(true, $"Cluster '{cluster.ClusterId}' {action}");
        });
    }

    #endregion

    #region TryAddCluster (basic overload)

    public async Task<RouteOperationResult> TryAddCluster(string clusterId, Dictionary<string, string> destinations,
        string? loadBalancingPolicy, Models.HealthCheckConfig? healthCheck, string source, string? createdBy)
    {
        if (string.IsNullOrWhiteSpace(clusterId))
            return new RouteOperationResult(false, "Cluster ID cannot be empty");
        if (destinations == null || destinations.Count == 0)
        { AuditLog.RecordFailure("AddCluster", clusterId, "At least one destination is required"); return new RouteOperationResult(false, "At least one destination is required"); }

        return await ExecuteWithLockAsync(
            "AddCluster", clusterId, async config =>
        {
            var cc = new ClusterConfig
            {
                ClusterId = clusterId,
                Destinations = destinations.ToDictionary(d => d.Key, d => new DestinationConfig { Address = d.Value }),
                LoadBalancingPolicy = loadBalancingPolicy,
                HealthCheck = DynamicYarpConfigHelpers.BuildClusterHealthCheck(healthCheck)
            };

            var dc = config.Clusters.FirstOrDefault(c =>
                string.Equals(c.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));

            bool isNew;
            if (dc == null)
            {
                isNew = true;
                config.Clusters.Add(new DynamicClusterConfig
                { Config = cc, HealthCheck = healthCheck, Source = source, CreatedAt = DateTime.UtcNow, CreatedBy = createdBy });
            }
            else
            {
                isNew = false;
                dc.Config = cc;
                dc.HealthCheck = healthCheck;
                if (!string.IsNullOrEmpty(source) && source != dc.Source)
                { dc.Source = source; dc.CreatedBy = createdBy; }
            }

            var action = isNew ? "created" : "updated";
            _logger.LogInformation("Cluster '{ClusterId}' {Action} with {DestCount} destinations", clusterId, action, destinations.Count);
            AuditLog.RecordSuccess(isNew ? "AddCluster" : "UpdateCluster", clusterId, createdBy, null, null,
                new { destinations, loadBalancingPolicy });
            return new RouteOperationResult(true, $"Cluster '{clusterId}' {action}");
        });
    }

    #endregion

    #region TryAddCluster (CreateClusterRequest)

    public async Task<RouteOperationResult> TryAddCluster(CreateClusterRequest request,
        string source, string? createdBy)
    {
        return await ExecuteWithLockAsync(
            "CreateCluster", request.ClusterId, async config =>
        {
            if (config.Clusters.Any(c =>
                string.Equals(c.Config.ClusterId, request.ClusterId, StringComparison.OrdinalIgnoreCase)))
            {
                AuditLog.RecordFailure("AddCluster", request.ClusterId, $"Cluster '{request.ClusterId}' already exists", createdBy);
                return new RouteOperationResult(false, $"Cluster '{request.ClusterId}' already exists. Use update instead.");
            }

            var hc = request.HealthCheck;
            var dmHc = hc != null ? new Models.HealthCheckConfig
            {
                Active = hc.Active?.Enabled ?? false,
                Endpoint = hc.Active?.Path,
                Passive = hc.Passive?.Enabled ?? false,
                PassivePolicy = hc.Passive?.Policy,
                PassiveReactivationPeriod = TimeSpan.TryParse(hc.Passive?.ReactivationPeriod, out var rp) ? rp : TimeSpan.FromSeconds(30),
                AvailableDestinationsPolicy = hc.AvailableDestinationsPolicy
            } : null;

            var cc = new ClusterConfig
            {
                ClusterId = request.ClusterId,
                Destinations = request.Destinations.ToDictionary(d => d.Key, d => new DestinationConfig { Address = d.Value }),
                LoadBalancingPolicy = request.LoadBalancingPolicy,
                HealthCheck = DynamicYarpConfigHelpers.BuildClusterHealthCheck(dmHc)
            };

            config.Clusters.Add(new DynamicClusterConfig
            { Config = cc, HealthCheck = dmHc, Source = source, CreatedAt = DateTime.UtcNow, CreatedBy = createdBy });

            _logger.LogDebug("Cluster '{ClusterId}' created with {DestCount} destinations", request.ClusterId, request.Destinations.Count);
            AuditLog.RecordSuccess("AddCluster", request.ClusterId, createdBy, null, null,
                new { destinations = request.Destinations, loadBalancingPolicy = request.LoadBalancingPolicy });
            return new RouteOperationResult(true, $"Cluster '{request.ClusterId}' created successfully");
        });
    }

    #endregion

    #region TryUpdateCluster

    public async Task<RouteOperationResult> TryUpdateCluster(string clusterId, UpdateClusterRequest request)
    {
        if (string.IsNullOrWhiteSpace(clusterId))
        { AuditLog.RecordFailure("UpdateCluster", clusterId ?? "", "Cluster ID cannot be empty"); return new RouteOperationResult(false, "Cluster ID cannot be empty"); }

        return await ExecuteWithLockAsync(
            "UpdateCluster", clusterId, async config =>
        {
            var dc = config.Clusters.FirstOrDefault(c =>
                string.Equals(c.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
            if (dc == null)
            { AuditLog.RecordFailure("UpdateCluster", clusterId, $"Cluster '{clusterId}' not found"); return new RouteOperationResult(false, $"Cluster '{clusterId}' not found"); }

            var existing = dc.Config;
            var hc = request.HealthCheck;
            Models.HealthCheckConfig? dmHc = null;
            if (hc != null)
            {
                dmHc = new Models.HealthCheckConfig
                {
                    Active = hc.Active?.Enabled ?? false,
                    Endpoint = hc.Active?.Path,
                    Passive = hc.Passive?.Enabled ?? false,
                    PassivePolicy = hc.Passive?.Policy,
                    PassiveReactivationPeriod = TimeSpan.TryParse(hc.Passive?.ReactivationPeriod, out var rp) ? rp : TimeSpan.FromSeconds(30),
                    AvailableDestinationsPolicy = hc.AvailableDestinationsPolicy
                };
            }

            dc.Config = new ClusterConfig
            {
                ClusterId = existing.ClusterId,
                Destinations = request.Destinations?.ToDictionary(d => d.Key, d => new DestinationConfig { Address = d.Value }) ?? existing.Destinations,
                LoadBalancingPolicy = request.LoadBalancingPolicy ?? existing.LoadBalancingPolicy,
                HealthCheck = dmHc != null
                    ? DynamicYarpConfigHelpers.BuildClusterHealthCheck(dmHc)
                    : existing.HealthCheck
            };
            if (dmHc != null) dc.HealthCheck = dmHc;

            _logger.LogDebug("Cluster '{ClusterId}' updated", clusterId);
            AuditLog.RecordSuccess("UpdateCluster", clusterId, null, null, null,
                new { destinations = request.Destinations, loadBalancingPolicy = request.LoadBalancingPolicy });
            return new RouteOperationResult(true, $"Cluster '{clusterId}' updated successfully");
        });
    }

    #endregion

    #region TryRemoveCluster

    public async Task<RouteOperationResult> TryRemoveCluster(string clusterId)
    {
        if (string.IsNullOrWhiteSpace(clusterId))
            return new RouteOperationResult(false, "Cluster ID cannot be empty");

        return await ExecuteWithLockAsync(
            "RemoveCluster", clusterId, async config =>
        {
            var hasRef = config.Routes.Any(r =>
                string.Equals(r.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
            if (hasRef)
            { AuditLog.RecordFailure("RemoveCluster", clusterId, $"Cluster '{clusterId}' is referenced by route(s)"); return new RouteOperationResult(false, $"Cluster '{clusterId}' is referenced by route(s). Delete routes first."); }

            var cluster = config.Clusters.FirstOrDefault(c =>
                string.Equals(c.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
            if (cluster == null)
            { AuditLog.RecordFailure("RemoveCluster", clusterId, $"Cluster '{clusterId}' not found"); return new RouteOperationResult(false, $"Cluster '{clusterId}' not found"); }

            config.Clusters.Remove(cluster);

            _logger.LogInformation("Cluster '{ClusterId}' deleted", clusterId);
            AuditLog.RecordSuccess("RemoveCluster", clusterId, null, null, new { destinations = cluster.Config.Destinations?.Count });
            return new RouteOperationResult(true, $"Cluster '{clusterId}' deleted");
        });
    }

    #endregion

    #region TryRenameCluster

    public async Task<RouteOperationResult> TryRenameCluster(string oldClusterId, string newClusterId,
        Dictionary<string, string> destinations, string? loadBalancingPolicy,
        Models.HealthCheckConfig? healthCheck, string source, string? createdBy)
    {
        if (string.IsNullOrWhiteSpace(oldClusterId) || string.IsNullOrWhiteSpace(newClusterId))
        { AuditLog.RecordFailure("UpdateCluster", oldClusterId ?? "", "Cluster ID cannot be empty"); return new RouteOperationResult(false, "Cluster ID cannot be empty"); }
        if (string.Equals(oldClusterId, newClusterId, StringComparison.OrdinalIgnoreCase))
        { AuditLog.RecordFailure("UpdateCluster", oldClusterId, "Old and new cluster IDs are the same"); return new RouteOperationResult(false, "Old and new cluster IDs are the same"); }

        return await ExecuteWithLockAsync(
            "UpdateCluster", oldClusterId, async config =>
        {
            var oldDc = config.Clusters.FirstOrDefault(c =>
                string.Equals(c.Config.ClusterId, oldClusterId, StringComparison.OrdinalIgnoreCase));
            if (oldDc == null)
            { AuditLog.RecordFailure("UpdateCluster", oldClusterId, $"Cluster '{oldClusterId}' not found"); return new RouteOperationResult(false, $"Cluster '{oldClusterId}' not found"); }
            if (config.Clusters.Any(c =>
                string.Equals(c.Config.ClusterId, newClusterId, StringComparison.OrdinalIgnoreCase)))
            { AuditLog.RecordFailure("UpdateCluster", oldClusterId, $"Cluster '{newClusterId}' already exists"); return new RouteOperationResult(false, $"Cluster '{newClusterId}' already exists"); }

            var newCluster = new ClusterConfig
            {
                ClusterId = newClusterId,
                Destinations = destinations.ToDictionary(d => d.Key, d => new DestinationConfig { Address = d.Value }),
                LoadBalancingPolicy = loadBalancingPolicy ?? oldDc.Config.LoadBalancingPolicy,
                HealthCheck = healthCheck != null ? DynamicYarpConfigHelpers.BuildClusterHealthCheck(healthCheck) : oldDc.Config.HealthCheck
            };

            // Replace cluster entry, preserving UID
            config.Clusters.Remove(oldDc);
            config.Clusters.Add(new DynamicClusterConfig
            {
                Config = newCluster,
                ClusterUid = oldDc.ClusterUid,
                HealthCheck = healthCheck ?? oldDc.HealthCheck,
                CircuitBreaker = oldDc.CircuitBreaker,
                Source = source,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            });

            // Update all referencing routes
            int updatedRouteCount = 0;
            foreach (var dr in config.Routes.Where(r =>
                string.Equals(r.Config.ClusterId, oldClusterId, StringComparison.OrdinalIgnoreCase)))
            {
                dr.ClusterUid = oldDc.ClusterUid;
                dr.Config = dr.Config with { ClusterId = newClusterId };
                updatedRouteCount++;
            }

            _logger.LogInformation("Cluster '{OldId}' renamed to '{NewId}', updated {Count} routes", oldClusterId, newClusterId, updatedRouteCount);
            AuditLog.RecordSuccess("UpdateCluster", $"{oldClusterId} → {newClusterId}", createdBy, null,
                new { oldClusterId, routesUpdated = updatedRouteCount, action = "rename" },
                new { newClusterId, destinations, loadBalancingPolicy, action = "rename" });
            return new RouteOperationResult(true, $"Cluster '{oldClusterId}' renamed to '{newClusterId}', {updatedRouteCount} route(s) updated");
        });
    }

    #endregion

    #region UpdateClusterCircuitBreakerAsync

    public async Task<bool> UpdateClusterCircuitBreakerAsync(string clusterId, CircuitBreakerConfig? config)
    {
        if (string.IsNullOrWhiteSpace(clusterId)) return false;

        return await ExecuteMetadataWithLockAsync("UpdateClusterCircuitBreaker", clusterId, async state =>
        {
            var dc = state.Clusters.FirstOrDefault(c =>
                string.Equals(c.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
            if (dc == null)
            {
                _logger.LogWarning("UpdateClusterCircuitBreaker: cluster '{ClusterId}' not found", clusterId);
                return false;
            }
            dc.CircuitBreaker = config;
            _logger.LogDebug("Updated circuit breaker config for cluster '{ClusterId}': Enabled={Enabled}",
                clusterId, config?.Enabled ?? false);
            return true;
        });
    }

    #endregion

    #region Query methods (lock-free reads via volatile provider snapshot)

    public IReadOnlyList<ClusterConfig> GetClusters() => _configProvider.GetClusters();

    public ClusterConfig? GetCluster(string clusterId) => _configProvider.GetCluster(clusterId);

    #endregion
}
