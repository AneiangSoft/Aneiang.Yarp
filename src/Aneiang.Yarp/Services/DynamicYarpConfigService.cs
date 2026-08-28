using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services.ProxyConfigHealth;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Dynamic YARP config service: add, update, and delete routes and clusters at runtime.
/// Thread-safe with SemaphoreSlim protection. Delegates CRUD to <see cref="IRouteConfigManager"/>
/// and <see cref="IClusterConfigManager"/>, persistence to <see cref="IDynamicConfigPersister"/>,
/// and snapshot publishing to <see cref="IDynamicConfigPublisher"/>.
/// </summary>
public class DynamicYarpConfigService : IDynamicYarpConfigService, IHostedService
{
    private readonly ILogger<DynamicYarpConfigService> _logger;
    private readonly SemaphoreSlim _semaphore;

    private readonly DynamicConfigState _state;
    private readonly IDynamicConfigPersister _persister;
    private readonly IDynamicConfigPublisher _publisher;
    private readonly IRouteConfigManager _routeManager;
    private readonly IClusterConfigManager _clusterManager;
    private readonly IConfigChangeAuditLog _auditLog;
    private readonly AneiangProxyConfigProvider _configProvider;
    private readonly IGatewaySnapshotCompiler _snapshotCompiler;
    private readonly IGatewaySnapshotPublisher _snapshotPublisher;

    public DynamicYarpConfigService(
        AneiangProxyConfigProvider configProvider,
        IRouteRepository routeRepo,
        IClusterRepository clusterRepo,
        IConfigChangeAuditLog auditLog,
        ILoggerFactory loggerFactory,
        IGatewaySnapshotCompiler snapshotCompiler,
        IGatewaySnapshotPublisher snapshotPublisher,
        IConfigValidator configValidator,
        IProxyConfigErrorStore errorStore)
    {
        _auditLog = auditLog;
        _configProvider = configProvider;
        _snapshotCompiler = snapshotCompiler;
        _snapshotPublisher = snapshotPublisher;
        _logger = loggerFactory.CreateLogger<DynamicYarpConfigService>();
        _semaphore = new SemaphoreSlim(1, 1);
        _state = new DynamicConfigState();

        _persister = new DynamicConfigPersister(routeRepo, clusterRepo,
            loggerFactory.CreateLogger<DynamicConfigPersister>());
        _publisher = new DynamicConfigPublisher(
            configProvider,
            snapshotCompiler,
            snapshotPublisher,
            configValidator,
            errorStore,
            loggerFactory.CreateLogger<DynamicConfigPublisher>());

        _routeManager = new RouteConfigManager(_state, _semaphore, _persister, _publisher, auditLog,
            loggerFactory.CreateLogger<RouteConfigManager>());
        _clusterManager = new ClusterConfigManager(configProvider, _state, _semaphore, _persister, _publisher, auditLog,
            loggerFactory.CreateLogger<ClusterConfigManager>());

        // Capture static config from the provider's initial snapshot.
        var initialConfig = configProvider.GetConfig();
        _state.StaticRoutes = initialConfig.Routes ?? Array.Empty<RouteConfig>();
        _state.StaticClusters = initialConfig.Clusters ?? Array.Empty<ClusterConfig>();
    }

    #region IHostedService

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicYarpConfigService"/> class.
    /// </summary>
    async Task IHostedService.StartAsync(CancellationToken cancellationToken) => await LoadDynamicConfigAsync();

    /// <summary>
    /// Stops the hosted service.
    /// </summary>
    Task IHostedService.StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Loads the dynamic configuration from persistence on startup.
    /// </summary>
    private async Task LoadDynamicConfigAsync()
    {
        try
        {
            _state.Config = await _persister.LoadAsync();
            await MarkStaticConfigAsync();
            _publisher.Publish(_state.Config, _state.Version);
            _logger.LogInformation("Loaded {RouteCount} routes and {ClusterCount} clusters (dynamic + static) on startup",
                _state.Config.Routes.Count, _state.Config.Clusters.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dynamic config on startup");
            _state.Config = new GatewayDynamicConfig();
        }
    }

    /// <summary>
    /// Marks static routes and clusters from the appsettings config.
    /// </summary>
    private async Task MarkStaticConfigAsync()
    {
        try
        {
            _state.EnsureInitialized();
            var thisStartupStaticRoutes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var thisStartupStaticClusters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var route in _state.StaticRoutes)
                thisStartupStaticRoutes.Add(route.RouteId ?? string.Empty);

            // Only remove routes that came from appsettings.json (config source).
            // Routes added via Dashboard/Api (Source="dashboard"/"dynamic") must survive restart.
            _state.Config.Routes.RemoveAll(r =>
                r.Source == "config" && !thisStartupStaticRoutes.Contains(r.Config.RouteId ?? string.Empty));

            var config = _state.Config;
            foreach (var route in _state.StaticRoutes)
            {
                var routeId = route.RouteId ?? string.Empty;
                _state.StaticRouteIds.Add(routeId);
                var normalizedRoute = route.Order == null ? route with { Order = int.MaxValue } : route;
                var existing = config.Routes.FirstOrDefault(r =>
                    string.Equals(r.Config.RouteId, routeId, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                    config.Routes.Add(new DynamicRouteConfig { Config = normalizedRoute, ClusterUid = _state.ResolveClusterUid(route.ClusterId ?? string.Empty), Source = "config", CreatedAt = DateTime.Now, CreatedBy = "appsettings.json" });
                else { existing.Config = normalizedRoute; existing.ClusterUid = _state.ResolveClusterUid(route.ClusterId ?? string.Empty); }
            }

            foreach (var cluster in _state.StaticClusters)
                thisStartupStaticClusters.Add(cluster.ClusterId ?? string.Empty);

            // Only remove clusters that came from appsettings.json (config source).
            _state.Config.Clusters.RemoveAll(c =>
                c.Source == "config" && !thisStartupStaticClusters.Contains(c.Config.ClusterId ?? string.Empty));

            foreach (var cluster in _state.StaticClusters)
            {
                var clusterId = cluster.ClusterId ?? string.Empty;
                _state.StaticClusterIds.Add(clusterId);
                var existing = config.Clusters.FirstOrDefault(c =>
                    string.Equals(c.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                    config.Clusters.Add(new DynamicClusterConfig { Config = cluster, Source = "config", CreatedAt = DateTime.Now, CreatedBy = "appsettings.json" });
                else existing.Config = cluster;
            }

            await _persister.SaveAsync(_state.Config, "MarkStaticConfig");
            _logger.LogDebug("Synced {TotalRoutes} routes and {TotalClusters} clusters. Static route IDs: [{StaticRoutes}], Static cluster IDs: [{StaticClusters}]",
                config.Routes.Count, config.Clusters.Count, string.Join(", ", _state.StaticRouteIds), string.Join(", ", _state.StaticClusterIds));
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to mark static config"); }
    }

    #endregion

    #region IDynamicYarpConfigService delegation

    /// <inheritdoc />
    public IReadOnlyList<RouteConfig> GetRoutes() => _routeManager.GetRoutes();
    public IReadOnlyList<ClusterConfig> GetClusters() => _clusterManager.GetClusters();
    public ClusterConfig? GetCluster(string clusterId) => _clusterManager.GetCluster(clusterId);
    public GatewayDynamicConfig? GetDynamicConfig() => _state.Config;

    public Task<RouteOperationResult> TryAddRoute(RegisterRouteRequest request, string source = "dynamic", string? createdBy = null)
        => _routeManager.TryAddRoute(request, source, createdBy);

    public Task<RouteOperationResult> TryAddRouteConfig(RouteConfig route, string source = "dashboard", string? createdBy = "dashboard-user")
        => _routeManager.TryAddRouteConfig(route, source, createdBy);

    public Task<RouteOperationResult> TryRemoveRoute(string routeName, string? clientIp = null, bool removeOrphanedCluster = true)
        => _routeManager.TryRemoveRoute(routeName, clientIp, removeOrphanedCluster);

    public Task<RouteOperationResult> TryRenameRoute(string oldRouteId, string newRouteId, RegisterRouteRequest request, string source = "dashboard", string? createdBy = "dashboard-user")
        => _routeManager.TryRenameRoute(oldRouteId, newRouteId, request, source, createdBy);

    public Task<RouteOperationResult> TrySetRouteEnabled(string routeId, bool enabled, string? createdBy = "dashboard-user")
        => _routeManager.TrySetRouteEnabled(routeId, enabled, createdBy);

    public Task<RouteOperationResult> TryAddClusterConfig(ClusterConfig cluster, string source = "dashboard", string? createdBy = "dashboard-user")
        => _clusterManager.TryAddClusterConfig(cluster, source, createdBy);

    public Task<RouteOperationResult> TryAddCluster(string clusterId, Dictionary<string, string> destinations,
        string? loadBalancingPolicy = null, Models.HealthCheckConfig? healthCheck = null, string source = "dynamic", string? createdBy = null,
        Dictionary<string, string>? metadata = null)
        => _clusterManager.TryAddCluster(clusterId, destinations, loadBalancingPolicy, healthCheck, source, createdBy, metadata);

    public Task<RouteOperationResult> TryAddCluster(CreateClusterRequest request, string source = "dynamic", string? createdBy = null)
        => _clusterManager.TryAddCluster(request, source, createdBy);

    public Task<RouteOperationResult> TryUpdateCluster(string clusterId, UpdateClusterRequest request)
        => _clusterManager.TryUpdateCluster(clusterId, request);

    public Task<RouteOperationResult> TryRemoveCluster(string clusterId)
        => _clusterManager.TryRemoveCluster(clusterId);

    public Task<RouteOperationResult> TryRenameCluster(string oldClusterId, string newClusterId,
        Dictionary<string, string> destinations, string? loadBalancingPolicy = null,
        Models.HealthCheckConfig? healthCheck = null, string source = "dashboard", string? createdBy = "dashboard-user")
        => _clusterManager.TryRenameCluster(oldClusterId, newClusterId, destinations, loadBalancingPolicy, healthCheck, source, createdBy);

    #endregion

    #region RefreshConfig / SaveDynamicConfig

    /// <summary>
    /// Refreshes the configuration by republishing the current state.
    /// </summary>
    public void RefreshConfig()
    {
        _semaphore.Wait();
        try { _publisher.Publish(_state.Config, _state.Version); }
        finally { _semaphore.Release(); }
    }

    /// <summary>
    /// Saves the dynamic configuration to persistence.
    /// </summary>
    public async Task SaveDynamicConfig()
    {
        await _semaphore.WaitAsync();
        try { await _persister.SaveAsync(_state.Config, "SaveDynamicConfig"); }
        finally { _semaphore.Release(); }
    }

    #endregion

    #region ImportBatchAsync

    /// <summary>
    /// Batch add-or-update clusters and routes under a single lock with one
    /// version bump, one publish, and one persistence pass.
    /// </summary>
    public async Task<RouteOperationResult> ImportBatchAsync(
        IReadOnlyList<ClusterConfig> clusters, IReadOnlyList<RouteConfig> routes,
        string source = "import", string? createdBy = "dashboard-user")
    {
        if (clusters.Count == 0 && routes.Count == 0)
            return new RouteOperationResult(true, "Nothing to import");

        await _semaphore.WaitAsync();
        try
        {
            _state.EnsureInitialized();

            var clusterMap = _state.Config.Clusters.ToDictionary(
                c => c.Config.ClusterId ?? string.Empty, c => c, StringComparer.OrdinalIgnoreCase);

            foreach (var cluster in clusters)
            {
                var id = cluster.ClusterId ?? string.Empty;
                if (clusterMap.TryGetValue(id, out var existing))
                {
                    existing.Config = cluster;
                }
                else
                {
                    var dc = new DynamicClusterConfig
                        { Config = cluster, Source = source, CreatedAt = DateTime.Now, CreatedBy = createdBy };
                    _state.Config.Clusters.Add(dc);
                    clusterMap[id] = dc;
                }
            }

            var routeMap = _state.Config.Routes.ToDictionary(
                r => r.Config.RouteId ?? string.Empty, r => r, StringComparer.OrdinalIgnoreCase);

            foreach (var route in routes)
            {
                var normalized = route.Order == null ? route with { Order = int.MaxValue } : route;
                var id = normalized.RouteId ?? string.Empty;
                if (routeMap.TryGetValue(id, out var existing))
                {
                    existing.Config = normalized;
                    existing.ClusterUid = _state.ResolveClusterUid(normalized.ClusterId);
                }
                else
                {
                    var dr = new DynamicRouteConfig
                    {
                        Config = normalized,
                        ClusterUid = _state.ResolveClusterUid(normalized.ClusterId),
                        Source = source,
                        CreatedAt = DateTime.Now,
                        CreatedBy = createdBy
                    };
                    _state.Config.Routes.Add(dr);
                    routeMap[id] = dr;
                }
            }

            // Normalize comma-delimited transform values (same as single-route add).
            for (var ri = 0; ri < _state.Config.Routes.Count; ri++)
                _state.Config.Routes[ri].Config = DynamicYarpConfigHelpers.NormalizeTransforms(_state.Config.Routes[ri].Config);

            _logger.LogInformation("Batch import applied: {Clusters} cluster(s), {Routes} route(s)",
                clusters.Count, routes.Count);
            _auditLog.RecordSuccess("ImportConfig", "full config", createdBy, null, null,
                new { clusters = clusters.Count, routes = routes.Count });
            _state.IncrementVersion();
            _publisher.Publish(_state.Config, _state.Version);
            await _persister.SaveAsync(_state.Config, "ImportBatchAsync", "full config");
            return new RouteOperationResult(true,
                $"Imported {clusters.Count} cluster(s), {routes.Count} route(s)");
        }
        finally { _semaphore.Release(); }
    }

    #endregion

    #region Batch delete / enable (atomic, single lock+persist+publish)

    /// <inheritdoc />
    public async Task<BatchOperationResult> BatchDeleteClustersAsync(
        IReadOnlyList<string> clusterIds, string? createdBy = "dashboard-user")
    {
        if (clusterIds == null || clusterIds.Count == 0)
            return BatchOperationResult.From(Array.Empty<BatchItemResult>(), "No clusters to delete");

        await _semaphore.WaitAsync();
        try
        {
            _state.EnsureInitialized();

            var items = new List<BatchItemResult>(clusterIds.Count);
            var deletedClusters = new List<DynamicClusterConfig>();
            // Pre-resolve route-cluster reference counts so each cluster's
            // decision is independent: referenced → fail, else delete.
            var referencedClusterIds = new HashSet<string>(
                _state.Config.Routes
                    .Where(r => !string.IsNullOrWhiteSpace(r.Config.ClusterId))
                    .Select(r => r.Config.ClusterId!),
                StringComparer.OrdinalIgnoreCase);

            foreach (var rawId in clusterIds)
            {
                var id = rawId ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id))
                {
                    items.Add(new BatchItemResult(id, false, "Cluster ID cannot be empty"));
                    continue;
                }

                if (referencedClusterIds.Contains(id))
                {
                    _auditLog.RecordFailure("RemoveCluster", id, "Cluster is referenced by route(s)");
                    items.Add(new BatchItemResult(id, false, "Cluster is referenced by route(s); delete routes first"));
                    continue;
                }

                var cluster = _state.Config.Clusters.FirstOrDefault(c =>
                    string.Equals(c.Config.ClusterId, id, StringComparison.OrdinalIgnoreCase));
                if (cluster == null)
                {
                    items.Add(new BatchItemResult(id, false, "Cluster not found"));
                    continue;
                }

                _state.Config.Clusters.Remove(cluster);
                deletedClusters.Add(cluster);
                _auditLog.RecordSuccess("RemoveCluster", id, createdBy, null, null,
                    new { destinations = cluster.Config.Destinations?.Count });
                items.Add(new BatchItemResult(id, true, $"Cluster '{id}' deleted"));
            }

            if (deletedClusters.Count > 0)
            {
                _logger.LogInformation("Batch deleted {Count} cluster(s)", deletedClusters.Count);
                _state.IncrementVersion();
                _publisher.Publish(_state.Config, _state.Version);
                await SaveBestEffortAsync("BatchDeleteClusters");
            }

            return BatchOperationResult.From(items, "Batch delete clusters");
        }
        finally { _semaphore.Release(); }
    }

    /// <inheritdoc />
    public async Task<BatchOperationResult> BatchDeleteRoutesAsync(
        IReadOnlyList<string> routeIds, bool removeOrphanedClusters = false,
        string? createdBy = "dashboard-user")
    {
        if (routeIds == null || routeIds.Count == 0)
            return BatchOperationResult.From(Array.Empty<BatchItemResult>(), "No routes to delete");

        await _semaphore.WaitAsync();
        try
        {
            _state.EnsureInitialized();

            var items = new List<BatchItemResult>(routeIds.Count);
            var removedClusterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var survivors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rawId in routeIds)
            {
                var id = rawId ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id))
                {
                    items.Add(new BatchItemResult(id, false, "Route ID cannot be empty"));
                    continue;
                }

                var route = _state.Config.Routes.FirstOrDefault(r =>
                    string.Equals(r.Config.RouteId, id, StringComparison.OrdinalIgnoreCase));
                if (route == null)
                {
                    items.Add(new BatchItemResult(id, false, "Route not found"));
                    continue;
                }

                var clusterId = route.Config.ClusterId ?? string.Empty;
                _state.Config.Routes.Remove(route);
                _auditLog.RecordSuccess("RemoveRoute", id, createdBy, null, null,
                    new { clusterId });
                items.Add(new BatchItemResult(id, true, $"Route '{id}' deleted"));

                if (!string.IsNullOrWhiteSpace(clusterId))
                {
                    removedClusterIds.Add(clusterId);
                    survivors.UnionWith(_state.Config.Routes
                        .Where(r => !string.IsNullOrWhiteSpace(r.Config.ClusterId))
                        .Select(r => r.Config.ClusterId!));
                }
            }

            // Optional: clean up clusters left orphaned by the deleted routes.
            if (removeOrphanedClusters && removedClusterIds.Count > 0)
            {
                var orphaned = removedClusterIds
                    .Where(cid => !survivors.Contains(cid))
                    .Select(cid => _state.Config.Clusters.FirstOrDefault(c =>
                        string.Equals(c.Config.ClusterId, cid, StringComparison.OrdinalIgnoreCase)))
                    .Where(c => c != null)
                    .ToList();

                foreach (var orphan in orphaned)
                {
                    var cid = orphan!.Config.ClusterId ?? string.Empty;
                    _state.Config.Clusters.Remove(orphan);
                    _auditLog.RecordSuccess("RemoveCluster", cid, createdBy, null, null,
                        new { reason = "orphaned-after-batch-route-delete" });
                }
            }

            if (items.Count > 0)
            {
                _logger.LogInformation("Batch deleted {Count} route(s) (orphan cleanup: {Cleanup})",
                    items.Count(x => x.Success), removeOrphanedClusters);
                _state.IncrementVersion();
                _publisher.Publish(_state.Config, _state.Version);
                await SaveBestEffortAsync("BatchDeleteRoutes");
            }

            return BatchOperationResult.From(items, "Batch delete routes");
        }
        finally { _semaphore.Release(); }
    }

    /// <inheritdoc />
    public async Task<BatchOperationResult> BatchSetRoutesEnabledAsync(
        IReadOnlyList<string> routeIds, bool enabled, string? createdBy = "dashboard-user")
    {
        if (routeIds == null || routeIds.Count == 0)
            return BatchOperationResult.From(Array.Empty<BatchItemResult>(), "No routes to update");

        await _semaphore.WaitAsync();
        try
        {
            _state.EnsureInitialized();

            var items = new List<BatchItemResult>(routeIds.Count);
            var action = enabled ? "enabled" : "disabled";

            foreach (var rawId in routeIds)
            {
                var id = rawId ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id))
                {
                    items.Add(new BatchItemResult(id, false, "Route ID cannot be empty"));
                    continue;
                }

                var route = _state.Config.Routes.FirstOrDefault(r =>
                    string.Equals(r.Config.RouteId, id, StringComparison.OrdinalIgnoreCase));
                if (route == null)
                {
                    items.Add(new BatchItemResult(id, false, "Route not found"));
                    continue;
                }

                if (route.Enabled == enabled)
                {
                    items.Add(new BatchItemResult(id, true, $"Route '{id}' already {action}"));
                    continue;
                }

                route.Enabled = enabled;
                _auditLog.RecordSuccess("SetRouteEnabled", id, createdBy, null, null,
                    new { enabled });
                items.Add(new BatchItemResult(id, true, $"Route '{id}' {action}"));
            }

            var changed = items.Count(x => x.Success);
            if (changed > 0)
            {
                _logger.LogInformation("Batch {Action} {Count} route(s)", action, changed);
                _state.IncrementVersion();
                _publisher.Publish(_state.Config, _state.Version);
                await SaveBestEffortAsync("BatchSetRoutesEnabled");
            }

            return BatchOperationResult.From(items, $"Batch {action} routes");
        }
        finally { _semaphore.Release(); }
    }

    /// <summary>
    /// Best-effort persistence; in-memory state is authoritative. Mirrors the
    /// behavior of <see cref="ConfigManagerBase"/>: a save failure is logged,
    /// not propagated, so the in-memory publish remains in effect.
    /// </summary>
    private async Task SaveBestEffortAsync(string operationName)
    {
        try
        {
            await _persister.SaveAsync(_state.Config, operationName, "full config");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Persist failed for {Operation}", operationName);
        }
    }

    #endregion

    #region ReplaceAllConfig

    /// <summary>
    /// Replaces all routes and clusters in the configuration.
    /// </summary>
    /// <param name="newRoutes">The new routes.</param>
    /// <param name="newClusters">The new clusters.</param>
    /// <param name="source">The source.</param>
    /// <param name="createdBy">The created by.</param>
    /// <returns>A Task.</returns>
    public async Task ReplaceAllConfig(IReadOnlyList<RouteConfig> newRoutes, IReadOnlyList<ClusterConfig> newClusters,
        string source = "rollback", string? createdBy = "dashboard-user")
    {
        await _semaphore.WaitAsync();
        try
        {
            _state.EnsureInitialized();
            var existingClusters = _state.Config.Clusters.ToDictionary(c => c.Config.ClusterId ?? string.Empty, c => c, StringComparer.OrdinalIgnoreCase);
            var existingRoutes = _state.Config.Routes.ToDictionary(r => r.Config.RouteId ?? string.Empty, r => r, StringComparer.OrdinalIgnoreCase);
            _state.Config.Routes.Clear();
            _state.Config.Clusters.Clear();

            foreach (var cluster in newClusters)
            {
                if (existingClusters.TryGetValue(cluster.ClusterId ?? string.Empty, out var ec)) { ec.Config = cluster; _state.Config.Clusters.Add(ec); }
                else _state.Config.Clusters.Add(new DynamicClusterConfig { Config = cluster, Source = source, CreatedAt = DateTime.Now, CreatedBy = createdBy });
            }
            foreach (var route in newRoutes)
            {
                var nr = route.Order == null ? route with { Order = int.MaxValue } : route;
                if (existingRoutes.TryGetValue(route.RouteId ?? string.Empty, out var er)) { er.Config = nr; er.ClusterUid = _state.ResolveClusterUid(route.ClusterId); _state.Config.Routes.Add(er); }
                else _state.Config.Routes.Add(new DynamicRouteConfig { Config = nr, ClusterUid = _state.ResolveClusterUid(route.ClusterId), Source = source, CreatedAt = DateTime.Now, CreatedBy = createdBy });
            }

            _logger.LogInformation("Configuration replaced: {Routes} routes, {Clusters} clusters", newRoutes.Count, newClusters.Count);
            _auditLog.RecordSuccess("RollbackConfig", "full config", createdBy, null, null, new { routeCount = newRoutes.Count, clusterCount = newClusters.Count });
            _state.IncrementVersion();
            _publisher.Publish(_state.Config, _state.Version);
            await _persister.SaveAsync(_state.Config, "ReplaceAllConfig", "full config");
        }
        finally { _semaphore.Release(); }
    }

    #endregion

    #region Heartbeat

    /// <summary>
    /// Updates the heartbeat timestamp for a route's cluster.
    /// </summary>
    /// <param name="routeName">The route name.</param>
    /// <param name="clientIp">The client IP.</param>
    /// <returns>True if the heartbeat was updated.</returns>
    public bool UpdateHeartbeat(string routeName, string? clientIp = null)
    {
        _semaphore.Wait();
        try
        {
            _state.EnsureInitialized();
            var route = _state.Config.Routes.FirstOrDefault(r => (r.Config.RouteId ?? string.Empty).Equals(routeName, StringComparison.OrdinalIgnoreCase));
            if (route == null) return false;
            var cluster = _state.Config.Clusters.FirstOrDefault(c => (c.Config.ClusterId ?? string.Empty).Equals(route.Config.ClusterId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            if (cluster != null) { cluster.LastHeartbeat = DateTime.Now; return true; }
            return false;
        }
        finally { _semaphore.Release(); }
    }

    #endregion
}
