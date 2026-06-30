using Aneiang.Yarp.Models;
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

    public DynamicYarpConfigService(
        AneiangProxyConfigProvider configProvider,
        IRouteRepository routeRepo,
        IClusterRepository clusterRepo,
        IConfigChangeAuditLog auditLog,
        ILoggerFactory loggerFactory)
    {
        _auditLog = auditLog;
        _logger = loggerFactory.CreateLogger<DynamicYarpConfigService>();
        _semaphore = new SemaphoreSlim(1, 1);
        _state = new DynamicConfigState();

        _persister = new DynamicConfigPersister(routeRepo, clusterRepo,
            loggerFactory.CreateLogger<DynamicConfigPersister>());
        _publisher = new DynamicConfigPublisher(configProvider,
            loggerFactory.CreateLogger<DynamicConfigPublisher>());

        _routeManager = new RouteConfigManager(configProvider, _state, _semaphore, _persister, _publisher, auditLog,
            loggerFactory.CreateLogger<RouteConfigManager>());
        _clusterManager = new ClusterConfigManager(configProvider, _state, _semaphore, _persister, _publisher, auditLog,
            loggerFactory.CreateLogger<ClusterConfigManager>());

        // Capture static config from the provider's initial snapshot.
        var initialConfig = configProvider.GetConfig();
        _state.StaticRoutes = initialConfig.Routes ?? Array.Empty<RouteConfig>();
        _state.StaticClusters = initialConfig.Clusters ?? Array.Empty<ClusterConfig>();
    }

    // ── IHostedService ───────────────────────────────────────────────────

    async Task IHostedService.StartAsync(CancellationToken cancellationToken) => await LoadDynamicConfigAsync();
    Task IHostedService.StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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
                    config.Routes.Add(new DynamicRouteConfig { Config = normalizedRoute, ClusterUid = _state.ResolveClusterUid(route.ClusterId ?? string.Empty), Source = "config", CreatedAt = DateTime.UtcNow, CreatedBy = "appsettings.json" });
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
                    config.Clusters.Add(new DynamicClusterConfig { Config = cluster, Source = "config", CreatedAt = DateTime.UtcNow, CreatedBy = "appsettings.json" });
                else existing.Config = cluster;
            }

            await _persister.SaveAsync(_state.Config, "MarkStaticConfig");
            _logger.LogDebug("Synced {TotalRoutes} routes and {TotalClusters} clusters. Static route IDs: [{StaticRoutes}], Static cluster IDs: [{StaticClusters}]",
                config.Routes.Count, config.Clusters.Count, string.Join(", ", _state.StaticRouteIds), string.Join(", ", _state.StaticClusterIds));
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to mark static config"); }
    }

    // ── IDynamicYarpConfigService delegation ─────────────────────────────

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

    public Task<bool> UpdateRouteMetadataAsync(string routeId, Dictionary<string, string> metadata)
        => _routeManager.UpdateRouteMetadataAsync(routeId, metadata);

    public Task<RouteOperationResult> TryAddClusterConfig(ClusterConfig cluster, string source = "dashboard", string? createdBy = "dashboard-user")
        => _clusterManager.TryAddClusterConfig(cluster, source, createdBy);

    public Task<RouteOperationResult> TryAddCluster(string clusterId, Dictionary<string, string> destinations,
        string? loadBalancingPolicy = null, Models.HealthCheckConfig? healthCheck = null, string source = "dynamic", string? createdBy = null)
        => _clusterManager.TryAddCluster(clusterId, destinations, loadBalancingPolicy, healthCheck, source, createdBy);

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

    public Task<bool> UpdateClusterCircuitBreakerAsync(string clusterId, CircuitBreakerConfig? config)
        => _clusterManager.UpdateClusterCircuitBreakerAsync(clusterId, config);

    // ── RefreshConfig / SaveDynamicConfig ────────────────────────────────

    public void RefreshConfig()
    {
        _semaphore.Wait();
        try { _publisher.Publish(_state.Config, _state.Version); }
        finally { _semaphore.Release(); }
    }

    public async Task SaveDynamicConfig()
    {
        await _semaphore.WaitAsync();
        try { await _persister.SaveAsync(_state.Config, "SaveDynamicConfig"); }
        finally { _semaphore.Release(); }
    }

    // ── ReplaceAllConfig ─────────────────────────────────────────────────

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
                else _state.Config.Clusters.Add(new DynamicClusterConfig { Config = cluster, Source = source, CreatedAt = DateTime.UtcNow, CreatedBy = createdBy });
            }
            foreach (var route in newRoutes)
            {
                var nr = route.Order == null ? route with { Order = int.MaxValue } : route;
                if (existingRoutes.TryGetValue(route.RouteId ?? string.Empty, out var er)) { er.Config = nr; er.ClusterUid = _state.ResolveClusterUid(route.ClusterId); _state.Config.Routes.Add(er); }
                else _state.Config.Routes.Add(new DynamicRouteConfig { Config = nr, ClusterUid = _state.ResolveClusterUid(route.ClusterId), Source = source, CreatedAt = DateTime.UtcNow, CreatedBy = createdBy });
            }

            _logger.LogInformation("Configuration replaced: {Routes} routes, {Clusters} clusters", newRoutes.Count, newClusters.Count);
            _auditLog.RecordSuccess("RollbackConfig", "full config", createdBy, null, null, new { routeCount = newRoutes.Count, clusterCount = newClusters.Count });
            _state.IncrementVersion();
            _publisher.Publish(_state.Config, _state.Version);
            await _persister.SaveAsync(_state.Config, "ReplaceAllConfig", "full config");
        }
        finally { _semaphore.Release(); }
    }

    // ── Heartbeat ────────────────────────────────────────────────────────

    public bool UpdateHeartbeat(string routeName, string? clientIp = null)
    {
        _semaphore.Wait();
        try
        {
            _state.EnsureInitialized();
            var route = _state.Config.Routes.FirstOrDefault(r => (r.Config.RouteId ?? string.Empty).Equals(routeName, StringComparison.OrdinalIgnoreCase));
            if (route == null) return false;
            var cluster = _state.Config.Clusters.FirstOrDefault(c => (c.Config.ClusterId ?? string.Empty).Equals(route.Config.ClusterId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            if (cluster != null) { cluster.LastHeartbeat = DateTime.UtcNow; return true; }
            return false;
        }
        finally { _semaphore.Release(); }
    }
}
