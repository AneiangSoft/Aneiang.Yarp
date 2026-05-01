using Aneiang.Yarp.Models;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Dynamic YARP config service: add, update, and delete routes and clusters at runtime.
/// Thread-safe with reader-writer lock protection.
/// Supports persistence to gateway-dynamic.json.
/// </summary>
public class DynamicYarpConfigService
{
    private readonly InMemoryConfigProvider _configProvider;
    private readonly DynamicConfigPersistenceService _persistence;
    private readonly ILogger<DynamicYarpConfigService> _logger;
    private readonly object _lock = new();
    private GatewayDynamicConfig? _dynamicConfig;

    /// <summary>
    /// Initializes a new instance of DynamicYarpConfigService.
    /// </summary>
    /// <param name="configProvider">YARP in-memory config provider.</param>
    /// <param name="persistence">Dynamic config persistence service.</param>
    /// <param name="logger">Logger instance.</param>
    public DynamicYarpConfigService(
        InMemoryConfigProvider configProvider,
        DynamicConfigPersistenceService persistence,
        ILogger<DynamicYarpConfigService> logger)
    {
        _configProvider = configProvider;
        _persistence = persistence;
        _logger = logger;
        
        // Load dynamic config from file on startup
        LoadDynamicConfig();
    }

    /// <summary>
    /// Load dynamic configuration from persistence file on startup.
    /// </summary>
    private void LoadDynamicConfig()
    {
        try
        {
            _dynamicConfig = _persistence.LoadConfig();
            
            // Apply dynamic config to YARP
            if (_dynamicConfig != null && (_dynamicConfig.Routes.Count > 0 || _dynamicConfig.Clusters.Count > 0))
            {
                ApplyDynamicConfigToYarp();
                _logger.LogInformation(
                    "Loaded {RouteCount} dynamic routes and {ClusterCount} dynamic clusters from persistence",
                    _dynamicConfig.Routes.Count,
                    _dynamicConfig.Clusters.Count);
            }
            
            // Mark static config from appsettings.json as "config" source
            MarkStaticConfig();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dynamic config on startup");
            _dynamicConfig = new GatewayDynamicConfig();
        }
    }
    
    /// <summary>
    /// Mark routes and clusters from appsettings.json (static config) as "config" source.
    /// These should not be editable in the Dashboard.
    /// </summary>
    private void MarkStaticConfig()
    {
        try
        {
            EnsureDynamicConfigInitialized();
            var currentConfig = _configProvider.GetConfig();
            
            // Mark static routes
            if (currentConfig.Routes != null)
            {
                foreach (var route in currentConfig.Routes)
                {
                    var existingRoute = _dynamicConfig!.Routes.FirstOrDefault(r =>
                        string.Equals(r.RouteId, route.RouteId, StringComparison.OrdinalIgnoreCase));
                    
                    if (existingRoute == null)
                    {
                        // This route is from appsettings.json (static config)
                        _dynamicConfig.Routes.Add(new DynamicRouteConfig
                        {
                            RouteId = route.RouteId,
                            ClusterId = route.ClusterId ?? string.Empty,
                            MatchPath = route.Match?.Path ?? string.Empty,
                            Order = route.Order ?? 50,
                            Transforms = route.Transforms?.Select(t => new Dictionary<string, string>(t)).ToList(),
                            Source = "config",  // Mark as static config
                            CreatedAt = DateTime.UtcNow,
                            CreatedBy = "appsettings.json"
                        });
                    }
                }
            }
            
            // Mark static clusters
            if (currentConfig.Clusters != null)
            {
                foreach (var cluster in currentConfig.Clusters)
                {
                    var existingCluster = _dynamicConfig!.Clusters.FirstOrDefault(c =>
                        string.Equals(c.ClusterId, cluster.ClusterId, StringComparison.OrdinalIgnoreCase));
                    
                    if (existingCluster == null)
                    {
                        // This cluster is from appsettings.json (static config)
                        var destinations = cluster.Destinations?.ToDictionary(
                            d => d.Key,
                            d => d.Value.Address ?? string.Empty) ?? new Dictionary<string, string>();
                        
                        _dynamicConfig.Clusters.Add(new DynamicClusterConfig
                        {
                            ClusterId = cluster.ClusterId,
                            Destinations = destinations,
                            LoadBalancingPolicy = cluster.LoadBalancingPolicy,
                            Source = "config",  // Mark as static config
                            CreatedAt = DateTime.UtcNow,
                            CreatedBy = "appsettings.json"
                        });
                    }
                }
            }
            
            _logger.LogInformation(
                "Marked {TotalRoutes} routes and {TotalClusters} clusters (including static config from appsettings.json)",
                _dynamicConfig?.Routes.Count ?? 0,
                _dynamicConfig?.Clusters.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to mark static config");
        }
    }
    
    /// <summary>
    /// Apply dynamic configuration to YARP InMemoryConfigProvider.
    /// </summary>
    private void ApplyDynamicConfigToYarp()
    {
        if (_dynamicConfig == null) return;
        
        var currentConfig = _configProvider.GetConfig();
        var routes = currentConfig.Routes?.ToList() ?? new List<RouteConfig>();
        var clusters = currentConfig.Clusters?.ToList() ?? new List<ClusterConfig>();
        
        // Add dynamic routes
        foreach (var dynRoute in _dynamicConfig.Routes)
        {
            var existingIdx = routes.FindIndex(r => 
                string.Equals(r.RouteId, dynRoute.RouteId, StringComparison.OrdinalIgnoreCase));
            
            var routeConfig = new RouteConfig
            {
                RouteId = dynRoute.RouteId,
                ClusterId = dynRoute.ClusterId,
                Match = new RouteMatch { Path = dynRoute.MatchPath },
                Order = dynRoute.Order,
                Transforms = dynRoute.Transforms?.Select(t => (IReadOnlyDictionary<string, string>)t).ToList()
            };
            
            if (existingIdx >= 0)
                routes[existingIdx] = routeConfig;
            else
                routes.Add(routeConfig);
        }
        
        // Add dynamic clusters
        foreach (var dynCluster in _dynamicConfig.Clusters)
        {
            var existingIdx = clusters.FindIndex(c => 
                string.Equals(c.ClusterId, dynCluster.ClusterId, StringComparison.OrdinalIgnoreCase));
            
            var clusterConfig = new ClusterConfig
            {
                ClusterId = dynCluster.ClusterId,
                Destinations = dynCluster.Destinations.ToDictionary(
                    d => d.Key,
                    d => new DestinationConfig { Address = d.Value }),
                LoadBalancingPolicy = dynCluster.LoadBalancingPolicy
            };
            
            if (existingIdx >= 0)
                clusters[existingIdx] = clusterConfig;
            else
                clusters.Add(clusterConfig);
        }
        
        _configProvider.Update(routes, clusters);
    }
    
    /// <summary>
    /// Save dynamic configuration to persistence file.
    /// </summary>
    private void SaveDynamicConfig()
    {
        if (_dynamicConfig == null)
        {
            _dynamicConfig = new GatewayDynamicConfig();
        }
        
        _persistence.SaveConfig(_dynamicConfig);
    }

    /// <summary>
    /// Add or update a route (creates or replaces cluster). Thread-safe.
    /// </summary>
    /// <param name="request">Route registration request.</param>
    /// <param name="source">Configuration source: "dynamic" | "auto-register".</param>
    /// <param name="createdBy">Who created this route (optional).</param>
    /// <returns>Route operation result.</returns>
    public RouteOperationResult TryAddRoute(
        RegisterRouteRequest request, 
        string source = "dynamic",
        string? createdBy = null)
    {
        lock (_lock)
        {
            var config = _configProvider.GetConfig();
            var routes = config.Routes?.ToList() ?? new List<RouteConfig>();
            var clusters = config.Clusters?.ToList() ?? new List<ClusterConfig>();
            var newRoutes = new List<RouteConfig>(routes);
            var newClusters = new List<ClusterConfig>(clusters);

            var routeConfig = new RouteConfig
            {
                RouteId = request.RouteName,
                ClusterId = request.ClusterName,
                Match = new RouteMatch { Path = request.MatchPath },
                Order = request.Order ?? 50,
                Transforms = request.Transforms?.Select(t => (IReadOnlyDictionary<string, string>)t).ToList()
            };

            var existingRouteIdx = routes.FindIndex(r =>
                string.Equals(r.RouteId, request.RouteName, StringComparison.OrdinalIgnoreCase));

            bool isNew;
            if (existingRouteIdx >= 0)
            {
                newRoutes[existingRouteIdx] = routeConfig;
                isNew = false;
                _logger.LogInformation("Route '{RouteName}' exists, updating", request.RouteName);
            }
            else
            {
                newRoutes.Add(routeConfig);
                isNew = true;
                _logger.LogInformation("Route '{RouteName}' is new, adding", request.RouteName);
            }

            var clusterConfig = new ClusterConfig
            {
                ClusterId = request.ClusterName,
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    ["d1"] = new() { Address = request.DestinationAddress }
                }
            };

            var existingClusterIdx = newClusters.FindIndex(c =>
                string.Equals(c.ClusterId, request.ClusterName, StringComparison.OrdinalIgnoreCase));

            if (existingClusterIdx >= 0)
                newClusters[existingClusterIdx] = clusterConfig;
            else
                newClusters.Add(clusterConfig);

            _configProvider.Update(newRoutes, newClusters);
            
            // Save to dynamic config for persistence
            EnsureDynamicConfigInitialized();
            
            var dynRoute = _dynamicConfig!.Routes.FirstOrDefault(r => 
                string.Equals(r.RouteId, request.RouteName, StringComparison.OrdinalIgnoreCase));
            
            if (dynRoute == null)
            {
                dynRoute = new DynamicRouteConfig
                {
                    RouteId = request.RouteName,
                    ClusterId = request.ClusterName,
                    MatchPath = request.MatchPath,
                    Order = request.Order ?? 50,
                    Transforms = request.Transforms,
                    Source = source,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = createdBy
                };
                _dynamicConfig.Routes.Add(dynRoute);
            }
            else
            {
                dynRoute.ClusterId = request.ClusterName;
                dynRoute.MatchPath = request.MatchPath;
                dynRoute.Order = request.Order ?? 50;
                dynRoute.Transforms = request.Transforms;
            }
            
            // Update or add cluster in dynamic config
            var dynCluster = _dynamicConfig.Clusters.FirstOrDefault(c => 
                string.Equals(c.ClusterId, request.ClusterName, StringComparison.OrdinalIgnoreCase));
            
            if (dynCluster == null)
            {
                dynCluster = new DynamicClusterConfig
                {
                    ClusterId = request.ClusterName,
                    Destinations = new Dictionary<string, string> { ["d1"] = request.DestinationAddress },
                    Source = source,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = createdBy
                };
                _dynamicConfig.Clusters.Add(dynCluster);
            }
            else
            {
                dynCluster.Destinations["d1"] = request.DestinationAddress;
            }
            
            SaveDynamicConfig();

            var action = isNew ? "registered" : "updated";
            _logger.LogInformation("Route '{RouteName}' {Action} ({MatchPath} -> {Address})",
                request.RouteName, action, request.MatchPath, request.DestinationAddress);
            return new RouteOperationResult(true, $"Route '{request.RouteName}' {action}");
        }
    }

    /// <summary>
    /// Delete a route. Also removes the cluster if no remaining routes reference it.
    /// </summary>
    /// <param name="routeName">Route name to delete.</param>
    /// <returns>Route operation result.</returns>
    public RouteOperationResult TryRemoveRoute(string routeName)
    {
        if (string.IsNullOrWhiteSpace(routeName))
            return new RouteOperationResult(false, "Route name cannot be empty");

        lock (_lock)
        {
            var config = _configProvider.GetConfig();
            var routes = config.Routes?.ToList() ?? new List<RouteConfig>();
            var clusters = config.Clusters?.ToList() ?? new List<ClusterConfig>();

            var route = routes.FirstOrDefault(r =>
                string.Equals(r.RouteId, routeName, StringComparison.OrdinalIgnoreCase));
            if (route == null)
                return new RouteOperationResult(false, $"Route '{routeName}' not found");

            var newRoutes = routes.Where(r =>
                !string.Equals(r.RouteId, routeName, StringComparison.OrdinalIgnoreCase)).ToList();

            var clusterId = route.ClusterId;
            var orphaned = clusterId != null && !newRoutes.Any(r =>
                string.Equals(r.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));

            var newClusters = orphaned
                ? clusters.Where(c => !string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase)).ToList()
                : new List<ClusterConfig>(clusters);

            _configProvider.Update(newRoutes, newClusters);
            
            // Remove from dynamic config
            EnsureDynamicConfigInitialized();
            _dynamicConfig!.Routes.RemoveAll(r => 
                string.Equals(r.RouteId, routeName, StringComparison.OrdinalIgnoreCase));
            
            if (orphaned && clusterId != null)
            {
                _dynamicConfig.Clusters.RemoveAll(c => 
                    string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
            }
            
            SaveDynamicConfig();
            
            _logger.LogInformation("Route '{RouteName}' deleted", routeName);
            return new RouteOperationResult(true, $"Route '{routeName}' deleted");
        }
    }

    /// <summary>
    /// Delete a cluster if no routes reference it.
    /// </summary>
    /// <param name="clusterId">Cluster ID to delete.</param>
    /// <returns>Route operation result.</returns>
    public RouteOperationResult TryRemoveCluster(string clusterId)
    {
        if (string.IsNullOrWhiteSpace(clusterId))
            return new RouteOperationResult(false, "Cluster ID cannot be empty");

        lock (_lock)
        {
            var config = _configProvider.GetConfig();
            var routes = config.Routes?.ToList() ?? new List<RouteConfig>();
            var clusters = config.Clusters?.ToList() ?? new List<ClusterConfig>();

            // Check if any routes reference this cluster
            var referencingRoutes = routes.Where(r =>
                string.Equals(r.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase)).ToList();

            if (referencingRoutes.Count > 0)
            {
                return new RouteOperationResult(false,
                    $"Cluster '{clusterId}' is referenced by {referencingRoutes.Count} route(s). Delete routes first.");
            }

            var cluster = clusters.FirstOrDefault(c =>
                string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
            if (cluster == null)
                return new RouteOperationResult(false, $"Cluster '{clusterId}' not found");

            var newClusters = clusters.Where(c =>
                !string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase)).ToList();

            _configProvider.Update(routes, newClusters);
            
            // Remove from dynamic config
            EnsureDynamicConfigInitialized();
            _dynamicConfig!.Clusters.RemoveAll(c => 
                string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
            
            SaveDynamicConfig();
            
            _logger.LogInformation("Cluster '{ClusterId}' deleted", clusterId);
            return new RouteOperationResult(true, $"Cluster '{clusterId}' deleted");
        }
    }

    /// <summary>
    /// Get all routes from the current YARP configuration.
    /// </summary>
    /// <returns>Read-only list of route configurations.</returns>
    public IReadOnlyList<RouteConfig> GetRoutes() => _configProvider.GetConfig().Routes ?? Array.Empty<RouteConfig>();

    /// <summary>
    /// Get all clusters from the current YARP configuration.
    /// </summary>
    /// <returns>Read-only list of cluster configurations.</returns>
    public IReadOnlyList<ClusterConfig> GetClusters() => _configProvider.GetConfig().Clusters ?? Array.Empty<ClusterConfig>();
    
    /// <summary>
    /// Get dynamic configuration metadata.
    /// </summary>
    public GatewayDynamicConfig? GetDynamicConfig() => _dynamicConfig;
    
    /// <summary>
    /// Ensure dynamic config is initialized.
    /// </summary>
    private void EnsureDynamicConfigInitialized()
    {
        if (_dynamicConfig == null)
        {
            _dynamicConfig = new GatewayDynamicConfig();
        }
    }
}
