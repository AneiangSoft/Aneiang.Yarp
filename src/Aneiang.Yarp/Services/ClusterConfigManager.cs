using Aneiang.Yarp.Models;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>
/// All cluster CRUD operations on the dynamic config working set. Thread-safe via a shared
/// <see cref="SemaphoreSlim"/> (shared with <see cref="RouteConfigManager"/>).
/// </summary>
internal class ClusterConfigManager : IClusterConfigManager
{
    private readonly AneiangProxyConfigProvider _configProvider;
    private readonly DynamicConfigState _state;
    private readonly SemaphoreSlim _semaphore;
    private readonly IDynamicConfigPersister _persister;
    private readonly IDynamicConfigPublisher _publisher;
    private readonly IConfigChangeAuditLog _auditLog;
    private readonly ILogger<ClusterConfigManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClusterConfigManager"/> class.
    /// </summary>
    /// <param name="configProvider">The config provider.</param>
    /// <param name="state">The state.</param>
    /// <param name="semaphore">The semaphore.</param>
    /// <param name="persister">The persister.</param>
    /// <param name="publisher">The publisher.</param>
    /// <param name="auditLog">The audit log.</param>
    /// <param name="logger">The logger.</param>
    public ClusterConfigManager(
        AneiangProxyConfigProvider configProvider,
        DynamicConfigState state,
        SemaphoreSlim semaphore,
        IDynamicConfigPersister persister,
        IDynamicConfigPublisher publisher,
        IConfigChangeAuditLog auditLog,
        ILogger<ClusterConfigManager> logger)
    {
        _configProvider = configProvider;
        _state = state;
        _semaphore = semaphore;
        _persister = persister;
        _publisher = publisher;
        _auditLog = auditLog;
        _logger = logger;
    }


    #region TryAddClusterConfig (full native config)

    /// <summary>
    /// Tries the add cluster config.
    /// </summary>
    /// <param name="cluster">The cluster.</param>
    /// <param name="source">The source.</param>
    /// <param name="createdBy">The created by.</param>
    /// <returns>A Task.</returns>
    public async Task<RouteOperationResult> TryAddClusterConfig(
        ClusterConfig cluster, string source = "dashboard", string? createdBy = "dashboard-user")
    {
        if (string.IsNullOrWhiteSpace(cluster.ClusterId))
            return new RouteOperationResult(false, "Cluster ID cannot be empty");
        if (cluster.Destinations == null || cluster.Destinations.Count == 0)
        {
            _auditLog.RecordFailure("AddCluster", cluster.ClusterId, "At least one destination is required");
            return new RouteOperationResult(false, "At least one destination is required");
        }

        bool saveNeeded = false; bool isNew = false;
        await _semaphore.WaitAsync();
        try
        {
            var config = _configProvider.GetConfig();
            var newClusters = new List<ClusterConfig>(config.Clusters ?? []);
            var existingIdx = newClusters.FindIndex(c =>
                string.Equals(c.ClusterId, cluster.ClusterId, StringComparison.OrdinalIgnoreCase));
            if (existingIdx >= 0) { newClusters[existingIdx] = cluster; isNew = false; }
            else { newClusters.Add(cluster); isNew = true; }

            _state.EnsureInitialized();
            var dynCluster = _state.Config.Clusters.FirstOrDefault(c =>
                string.Equals(c.Config.ClusterId, cluster.ClusterId, StringComparison.OrdinalIgnoreCase));
            if (dynCluster == null)
            {
                _state.Config.Clusters.Add(new DynamicClusterConfig
                { Config = cluster, Source = source, CreatedAt = DateTime.UtcNow, CreatedBy = createdBy });
            }
            else
            {
                dynCluster.Config = cluster;
                if (!string.IsNullOrEmpty(source) && source != dynCluster.Source)
                { dynCluster.Source = source; dynCluster.CreatedBy = createdBy; }
            }
            saveNeeded = true;
            var action = isNew ? "created" : "updated";
            _logger.LogInformation("Cluster '{ClusterId}' {Action} via full config with {DestCount} destinations",
                cluster.ClusterId, action, cluster.Destinations.Count);
            _auditLog.RecordSuccess(isNew ? "AddCluster" : "UpdateCluster", cluster.ClusterId, createdBy, null, null,
                new { destinations = cluster.Destinations.ToDictionary(d => d.Key, d => d.Value.Address), loadBalancingPolicy = cluster.LoadBalancingPolicy });
            return new RouteOperationResult(true, $"Cluster '{cluster.ClusterId}' {action}");
        }
        finally
        {
            if (saveNeeded)
            {
                _state.IncrementVersion();
                _publisher.Publish(_state.Config, _state.Version);
                await _persister.SaveAsync(_state.Config, isNew ? "AddCluster" : "UpdateCluster", cluster.ClusterId);
            }
            _semaphore.Release();
        }
    }

    #endregion

    #region TryAddCluster (basic overload)

    /// <summary>
    /// Tries the add cluster.
    /// </summary>
    /// <param name="clusterId">The cluster id.</param>
    /// <param name="destinations">The destinations.</param>
    /// <param name="loadBalancingPolicy">The load balancing policy.</param>
    /// <param name="healthCheck">The health check.</param>
    /// <param name="source">The source.</param>
    /// <param name="createdBy">The created by.</param>
    /// <returns>A Task.</returns>
    public async Task<RouteOperationResult> TryAddCluster(string clusterId, Dictionary<string, string> destinations,
        string? loadBalancingPolicy = null, Models.HealthCheckConfig? healthCheck = null,
        string source = "dynamic", string? createdBy = null)
    {
        if (string.IsNullOrWhiteSpace(clusterId))
            return new RouteOperationResult(false, "Cluster ID cannot be empty");
        if (destinations == null || destinations.Count == 0)
        { _auditLog.RecordFailure("AddCluster", clusterId, "At least one destination is required"); return new RouteOperationResult(false, "At least one destination is required"); }

        bool saveNeeded = false; bool isNew = false;
        await _semaphore.WaitAsync();
        try
        {
            var config = _configProvider.GetConfig();
            var newClusters = new List<ClusterConfig>(config.Clusters ?? []);
            var cc = new ClusterConfig
            {
                ClusterId = clusterId,
                Destinations = destinations.ToDictionary(d => d.Key, d => new DestinationConfig { Address = d.Value }),
                LoadBalancingPolicy = loadBalancingPolicy,
                HealthCheck = DynamicYarpConfigHelpers.BuildClusterHealthCheck(healthCheck)
            };
            var ei = newClusters.FindIndex(c => string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
            if (ei >= 0) { newClusters[ei] = cc; isNew = false; } else { newClusters.Add(cc); isNew = true; }

            _state.EnsureInitialized();
            var dc = _state.Config.Clusters.FirstOrDefault(c => string.Equals(c.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
            if (dc == null)
            {
                _state.Config.Clusters.Add(new DynamicClusterConfig
                { Config = cc, HealthCheck = healthCheck, Source = source, CreatedAt = DateTime.UtcNow, CreatedBy = createdBy });
            }
            else
            {
                dc.Config = cc; dc.HealthCheck = healthCheck;
                if (!string.IsNullOrEmpty(source) && source != dc.Source) { dc.Source = source; dc.CreatedBy = createdBy; }
            }
            saveNeeded = true;
            var action = isNew ? "created" : "updated";
            _logger.LogInformation("Cluster '{ClusterId}' {Action} with {DestCount} destinations", clusterId, action, destinations.Count);
            _auditLog.RecordSuccess(isNew ? "AddCluster" : "UpdateCluster", clusterId, createdBy, null, null,
                new { destinations, loadBalancingPolicy });
            return new RouteOperationResult(true, $"Cluster '{clusterId}' {action}");
        }
        finally
        {
            if (saveNeeded)
            {
                _state.IncrementVersion();
                _publisher.Publish(_state.Config, _state.Version);
                await _persister.SaveAsync(_state.Config, isNew ? "AddCluster" : "UpdateCluster", clusterId);
            }
            _semaphore.Release();
        }
    }

    #endregion

    #region TryAddCluster (CreateClusterRequest)

    /// <summary>
    /// Tries the add cluster.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="source">The source.</param>
    /// <param name="createdBy">The created by.</param>
    /// <returns>A Task.</returns>
    public async Task<RouteOperationResult> TryAddCluster(CreateClusterRequest request,
        string source = "dynamic", string? createdBy = null)
    {
        bool saveNeeded = false;
        await _semaphore.WaitAsync();
        try
        {
            var config = _configProvider.GetConfig();
            var clusters = config.Clusters ?? Array.Empty<ClusterConfig>();
            if (clusters.Any(c => string.Equals(c.ClusterId, request.ClusterId, StringComparison.OrdinalIgnoreCase)))
            {
                _auditLog.RecordFailure("AddCluster", request.ClusterId, $"Cluster '{request.ClusterId}' already exists", createdBy);
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
            var newClusters = new List<ClusterConfig>(clusters) { cc };

            _state.EnsureInitialized();
            _state.Config.Clusters.Add(new DynamicClusterConfig
            { Config = cc, HealthCheck = dmHc, Source = source, CreatedAt = DateTime.UtcNow, CreatedBy = createdBy });

            saveNeeded = true;
            _logger.LogDebug("Cluster '{ClusterId}' created with {DestCount} destinations", request.ClusterId, request.Destinations.Count);
            _auditLog.RecordSuccess("AddCluster", request.ClusterId, createdBy, null, null,
                new { destinations = request.Destinations, loadBalancingPolicy = request.LoadBalancingPolicy });
            return new RouteOperationResult(true, $"Cluster '{request.ClusterId}' created successfully");
        }
        finally
        {
            if (saveNeeded)
            {
                _state.IncrementVersion();
                _publisher.Publish(_state.Config, _state.Version);
                await _persister.SaveAsync(_state.Config, "CreateCluster", request.ClusterId);
            }
            _semaphore.Release();
        }
    }

    #endregion

    #region TryUpdateCluster

    /// <summary>
    /// Tries the update cluster.
    /// </summary>
    /// <param name="clusterId">The cluster id.</param>
    /// <param name="request">The request.</param>
    /// <returns>A Task.</returns>
    public async Task<RouteOperationResult> TryUpdateCluster(string clusterId, UpdateClusterRequest request)
    {
        if (string.IsNullOrWhiteSpace(clusterId))
        { _auditLog.RecordFailure("UpdateCluster", clusterId ?? "", "Cluster ID cannot be empty"); return new RouteOperationResult(false, "Cluster ID cannot be empty"); }

        bool saveNeeded = false;
        await _semaphore.WaitAsync();
        try
        {
            var config = _configProvider.GetConfig();
            var newClusters = new List<ClusterConfig>(config.Clusters ?? []);
            var ei = newClusters.FindIndex(c => string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
            if (ei < 0)
            { _auditLog.RecordFailure("UpdateCluster", clusterId, $"Cluster '{clusterId}' not found"); return new RouteOperationResult(false, $"Cluster '{clusterId}' not found"); }

            var existing = newClusters[ei];
            var hc = request.HealthCheck;
            var updated = new ClusterConfig
            {
                ClusterId = existing.ClusterId,
                Destinations = request.Destinations?.ToDictionary(d => d.Key, d => new DestinationConfig { Address = d.Value }) ?? existing.Destinations,
                LoadBalancingPolicy = request.LoadBalancingPolicy ?? existing.LoadBalancingPolicy,
                HealthCheck = hc != null
                    ? DynamicYarpConfigHelpers.BuildClusterHealthCheck(new Models.HealthCheckConfig
                    {
                        Active = hc.Active?.Enabled ?? false,
                        Endpoint = hc.Active?.Path,
                        Passive = hc.Passive?.Enabled ?? false,
                        PassivePolicy = hc.Passive?.Policy,
                        PassiveReactivationPeriod = TimeSpan.TryParse(hc.Passive?.ReactivationPeriod, out var rp) ? rp : TimeSpan.FromSeconds(30),
                        AvailableDestinationsPolicy = hc.AvailableDestinationsPolicy
                    }) : existing.HealthCheck
            };
            newClusters[ei] = updated;

            _state.EnsureInitialized();
            var dc = _state.Config.Clusters.FirstOrDefault(c => string.Equals(c.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
            if (dc != null)
            {
                dc.Config = updated;
                if (hc != null) dc.HealthCheck = new Models.HealthCheckConfig
                {
                    Active = hc.Active?.Enabled ?? false,
                    Endpoint = hc.Active?.Path,
                    Passive = hc.Passive?.Enabled ?? false,
                    PassivePolicy = hc.Passive?.Policy,
                    PassiveReactivationPeriod = TimeSpan.TryParse(hc.Passive?.ReactivationPeriod, out var rp2) ? rp2 : TimeSpan.FromSeconds(30),
                    AvailableDestinationsPolicy = hc.AvailableDestinationsPolicy
                };
            }
            saveNeeded = true;
            _logger.LogDebug("Cluster '{ClusterId}' updated", clusterId);
            _auditLog.RecordSuccess("UpdateCluster", clusterId, null, null, null,
                new { destinations = request.Destinations, loadBalancingPolicy = request.LoadBalancingPolicy });
            return new RouteOperationResult(true, $"Cluster '{clusterId}' updated successfully");
        }
        finally
        {
            if (saveNeeded)
            {
                _state.IncrementVersion();
                _publisher.Publish(_state.Config, _state.Version);
                await _persister.SaveAsync(_state.Config, "UpdateCluster", clusterId);
            }
            _semaphore.Release();
        }
    }

    #endregion

    #region TryRemoveCluster

    /// <summary>
    /// Tries the remove cluster.
    /// </summary>
    /// <param name="clusterId">The cluster id.</param>
    /// <returns>A Task.</returns>
    public async Task<RouteOperationResult> TryRemoveCluster(string clusterId)
    {
        if (string.IsNullOrWhiteSpace(clusterId)) return new RouteOperationResult(false, "Cluster ID cannot be empty");
        bool saveNeeded = false;
        await _semaphore.WaitAsync();
        try
        {
            var config = _configProvider.GetConfig();
            var routes = config.Routes ?? Array.Empty<RouteConfig>();
            var hasRef = routes.Any(r => string.Equals(r.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
            if (hasRef)
            { _auditLog.RecordFailure("RemoveCluster", clusterId, $"Cluster '{clusterId}' is referenced by route(s)"); return new RouteOperationResult(false, $"Cluster '{clusterId}' is referenced by route(s). Delete routes first."); }

            var cluster = (config.Clusters ?? Array.Empty<ClusterConfig>()).FirstOrDefault(c => string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
            if (cluster == null)
            { _auditLog.RecordFailure("RemoveCluster", clusterId, $"Cluster '{clusterId}' not found"); return new RouteOperationResult(false, $"Cluster '{clusterId}' not found"); }

            _state.EnsureInitialized();
            _state.Config.Clusters.RemoveAll(c => string.Equals(c.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
            saveNeeded = true;
            _logger.LogInformation("Cluster '{ClusterId}' deleted", clusterId);
            _auditLog.RecordSuccess("RemoveCluster", clusterId, null, null, new { destinations = cluster.Destinations?.Count });
            return new RouteOperationResult(true, $"Cluster '{clusterId}' deleted");
        }
        finally
        {
            if (saveNeeded)
            {
                _state.IncrementVersion();
                _publisher.Publish(_state.Config, _state.Version);
                await _persister.SaveAsync(_state.Config, "RemoveCluster", clusterId);
            }
            _semaphore.Release();
        }
    }

    #endregion

    #region TryRenameCluster

    /// <summary>
    /// Tries the rename cluster.
    /// </summary>
    /// <param name="oldClusterId">The old cluster id.</param>
    /// <param name="newClusterId">The new cluster id.</param>
    /// <param name="destinations">The destinations.</param>
    /// <param name="loadBalancingPolicy">The load balancing policy.</param>
    /// <param name="healthCheck">The health check.</param>
    /// <param name="source">The source.</param>
    /// <param name="createdBy">The created by.</param>
    /// <returns>A Task.</returns>
    public async Task<RouteOperationResult> TryRenameCluster(string oldClusterId, string newClusterId,
        Dictionary<string, string> destinations, string? loadBalancingPolicy = null,
        Models.HealthCheckConfig? healthCheck = null, string source = "dashboard", string? createdBy = "dashboard-user")
    {
        if (string.IsNullOrWhiteSpace(oldClusterId) || string.IsNullOrWhiteSpace(newClusterId))
        { _auditLog.RecordFailure("UpdateCluster", oldClusterId ?? "", "Cluster ID cannot be empty"); return new RouteOperationResult(false, "Cluster ID cannot be empty"); }
        if (string.Equals(oldClusterId, newClusterId, StringComparison.OrdinalIgnoreCase))
        { _auditLog.RecordFailure("UpdateCluster", oldClusterId, "Old and new cluster IDs are the same"); return new RouteOperationResult(false, "Old and new cluster IDs are the same"); }

        bool saveNeeded = false;
        await _semaphore.WaitAsync();
        try
        {
            var config = _configProvider.GetConfig();
            var newRoutes = new List<RouteConfig>(config.Routes ?? []);
            var newClusters = new List<ClusterConfig>(config.Clusters ?? []);

            var oldCluster = newClusters.FirstOrDefault(c => string.Equals(c.ClusterId, oldClusterId, StringComparison.OrdinalIgnoreCase));
            if (oldCluster == null)
            { _auditLog.RecordFailure("UpdateCluster", oldClusterId, $"Cluster '{oldClusterId}' not found"); return new RouteOperationResult(false, $"Cluster '{oldClusterId}' not found"); }
            if (newClusters.Any(c => string.Equals(c.ClusterId, newClusterId, StringComparison.OrdinalIgnoreCase)))
            { _auditLog.RecordFailure("UpdateCluster", oldClusterId, $"Cluster '{newClusterId}' already exists"); return new RouteOperationResult(false, $"Cluster '{newClusterId}' already exists"); }

            var newCluster = oldCluster with
            {
                ClusterId = newClusterId,
                Destinations = destinations.ToDictionary(d => d.Key, d => new DestinationConfig { Address = d.Value }),
                LoadBalancingPolicy = loadBalancingPolicy ?? oldCluster.LoadBalancingPolicy,
                HealthCheck = healthCheck != null ? DynamicYarpConfigHelpers.BuildClusterHealthCheck(healthCheck) : oldCluster.HealthCheck
            };
            newClusters.Add(newCluster);

            int updatedRouteCount = 0;
            for (int i = 0; i < newRoutes.Count; i++)
            {
                if (string.Equals(newRoutes[i].ClusterId, oldClusterId, StringComparison.OrdinalIgnoreCase))
                {
                    newRoutes[i] = new RouteConfig
                    {
                        RouteId = newRoutes[i].RouteId,
                        ClusterId = newClusterId,
                        Match = newRoutes[i].Match,
                        Order = newRoutes[i].Order,
                        Transforms = newRoutes[i].Transforms,
                        AuthorizationPolicy = newRoutes[i].AuthorizationPolicy,
                        CorsPolicy = newRoutes[i].CorsPolicy,
                        Metadata = newRoutes[i].Metadata
                    };
                    updatedRouteCount++;
                }
            }
            newClusters.RemoveAll(c => string.Equals(c.ClusterId, oldClusterId, StringComparison.OrdinalIgnoreCase));

            _state.EnsureInitialized();
            var oldDc = _state.Config.Clusters.FirstOrDefault(c => string.Equals(c.Config.ClusterId, oldClusterId, StringComparison.OrdinalIgnoreCase));
            _state.Config.Clusters.Add(new DynamicClusterConfig
            {
                Config = newCluster,
                ClusterUid = oldDc?.ClusterUid ?? Guid.NewGuid().ToString("N"),
                HealthCheck = healthCheck ?? oldDc?.HealthCheck,
                CircuitBreaker = oldDc?.CircuitBreaker,
                Source = source,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            });
            foreach (var dr in _state.Config.Routes.Where(r => string.Equals(r.Config.ClusterId, oldClusterId, StringComparison.OrdinalIgnoreCase)))
            {
                dr.ClusterUid = oldDc?.ClusterUid ?? _state.ResolveClusterUid(newClusterId);
                dr.Config = dr.Config with { ClusterId = newClusterId };
            }
            _state.Config.Clusters.RemoveAll(c => string.Equals(c.Config.ClusterId, oldClusterId, StringComparison.OrdinalIgnoreCase));

            saveNeeded = true;
            _logger.LogInformation("Cluster '{OldId}' renamed to '{NewId}', updated {Count} routes", oldClusterId, newClusterId, updatedRouteCount);
            _auditLog.RecordSuccess("UpdateCluster", $"{oldClusterId} → {newClusterId}", createdBy, null,
                new { oldClusterId, routesUpdated = updatedRouteCount, action = "rename" },
                new { newClusterId, destinations, loadBalancingPolicy, action = "rename" });
            return new RouteOperationResult(true, $"Cluster '{oldClusterId}' renamed to '{newClusterId}', {updatedRouteCount} route(s) updated");
        }
        finally
        {
            if (saveNeeded)
            {
                _state.IncrementVersion();
                _publisher.Publish(_state.Config, _state.Version);
                await _persister.SaveAsync(_state.Config, "UpdateCluster", oldClusterId);
            }
            _semaphore.Release();
        }
    }

    #endregion

    #region UpdateClusterCircuitBreakerAsync

    /// <summary>
    /// Updates the cluster circuit breaker async.
    /// </summary>
    /// <param name="clusterId">The cluster id.</param>
    /// <param name="config">The config.</param>
    /// <returns>A Task.</returns>
    public async Task<bool> UpdateClusterCircuitBreakerAsync(string clusterId, CircuitBreakerConfig? config)
    {
        if (string.IsNullOrWhiteSpace(clusterId)) return false;
        bool saveNeeded = false;
        await _semaphore.WaitAsync();
        try
        {
            _state.EnsureInitialized();
            var dc = _state.Config.Clusters.FirstOrDefault(c => string.Equals(c.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
            if (dc == null) { _logger.LogWarning("UpdateClusterCircuitBreaker: cluster '{ClusterId}' not found", clusterId); return false; }
            dc.CircuitBreaker = config;
            saveNeeded = true;
            _publisher.Publish(_state.Config, _state.Version);
            _logger.LogDebug("Updated circuit breaker config for cluster '{ClusterId}': Enabled={Enabled}", clusterId, config?.Enabled ?? false);
        }
        finally
        {
            if (saveNeeded)
            {
                _state.IncrementVersion();
                _publisher.Publish(_state.Config, _state.Version);
                await _persister.SaveAsync(_state.Config, "UpdateClusterCircuitBreaker", clusterId);
            }
            _semaphore.Release();
        }
        return true;
    }

    #endregion

    #region Query methods

    /// <summary>
    /// Gets the clusters.
    /// </summary>
    /// <returns>A list of ClusterConfigs.</returns>
    public IReadOnlyList<ClusterConfig> GetClusters()
    {
        _semaphore.Wait();
        try { return _configProvider.GetConfig().Clusters ?? Array.Empty<ClusterConfig>(); }
        finally { _semaphore.Release(); }
    }

    /// <summary>
    /// Gets the cluster.
    /// </summary>
    /// <param name="clusterId">The cluster id.</param>
    /// <returns>A ClusterConfig? .</returns>
    public ClusterConfig? GetCluster(string clusterId)
    {
        var clusters = GetClusters();
        return clusters.FirstOrDefault(c => string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
    }

    #endregion

}
