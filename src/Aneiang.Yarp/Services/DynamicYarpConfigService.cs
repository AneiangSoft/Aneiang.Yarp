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
    
    /// <summary>IDs of routes from appsettings.json (static config), populated on startup.</summary>
    private readonly HashSet<string> _staticRouteIds = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>IDs of clusters from appsettings.json (static config), populated on startup.</summary>
    private readonly HashSet<string> _staticClusterIds = new(StringComparer.OrdinalIgnoreCase);

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
    /// Record routes and clusters from appsettings.json (static config) IDs.
    /// Also ensures all static config items exist in _dynamicConfig for persistence.
    /// Does NOT override Source - all configs remain editable.
    /// </summary>
    private void MarkStaticConfig()
    {
        try
        {
            EnsureDynamicConfigInitialized();
            var currentConfig = _configProvider.GetConfig();
            
            // Record static routes
            if (currentConfig.Routes != null)
            {
                foreach (var route in currentConfig.Routes)
                {
                    // Record as static config ID
                    _staticRouteIds.Add(route.RouteId ?? string.Empty);
                    
                    var existingRoute = _dynamicConfig!.Routes.FirstOrDefault(r =>
                        string.Equals(r.RouteId, route.RouteId, StringComparison.OrdinalIgnoreCase));
                    
                    if (existingRoute == null)
                    {
                        // This route is from appsettings.json but not in dynamic config yet - add it
                        // Use "config" as initial source, but it will be updatable via dashboard
                        _dynamicConfig.Routes.Add(new DynamicRouteConfig
                        {
                            RouteId = route.RouteId,
                            ClusterId = route.ClusterId ?? string.Empty,
                            MatchPath = route.Match?.Path ?? string.Empty,
                            Order = route.Order ?? 50,
                            Transforms = route.Transforms?.Select(t => new Dictionary<string, string>(t)).ToList(),
                            Source = "config",
                            CreatedAt = DateTime.UtcNow,
                            CreatedBy = "appsettings.json"
                        });
                    }
                    // If route already exists in dynamic config, keep its current Source
                    // (may have been modified via dashboard and persisted)
                }
            }
            
            // Record static clusters
            if (currentConfig.Clusters != null)
            {
                foreach (var cluster in currentConfig.Clusters)
                {
                    // Record as static config ID
                    _staticClusterIds.Add(cluster.ClusterId ?? string.Empty);
                    
                    var existingCluster = _dynamicConfig!.Clusters.FirstOrDefault(c =>
                        string.Equals(c.ClusterId, cluster.ClusterId, StringComparison.OrdinalIgnoreCase));
                    
                    if (existingCluster == null)
                    {
                        // This cluster is from appsettings.json but not in dynamic config yet - add it
                        var destinations = cluster.Destinations?.ToDictionary(
                            d => d.Key,
                            d => d.Value.Address ?? string.Empty) ?? new Dictionary<string, string>();
                        
                        _dynamicConfig.Clusters.Add(new DynamicClusterConfig
                        {
                            ClusterId = cluster.ClusterId,
                            Destinations = destinations,
                            LoadBalancingPolicy = cluster.LoadBalancingPolicy,
                            Source = "config",
                            CreatedAt = DateTime.UtcNow,
                            CreatedBy = "appsettings.json"
                        });
                    }
                    // If cluster already exists in dynamic config, keep its current Source
                    // (may have been modified via dashboard and persisted)
                }
            }
            
            // Save the merged config (including static items that were just added)
            SaveDynamicConfig();
            
            _logger.LogInformation(
                "Recorded {TotalRoutes} routes and {TotalClusters} clusters (including static config from appsettings.json). Static route IDs: {StaticRoutes}, Static cluster IDs: {StaticClusters}",
                _dynamicConfig?.Routes.Count ?? 0,
                _dynamicConfig?.Clusters.Count ?? 0,
                string.Join(", ", _staticRouteIds),
                string.Join(", ", _staticClusterIds));
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
                // Update source when modified via dashboard (so it won't be overwritten as "config" on restart)
                if (!string.IsNullOrEmpty(source) && source != dynRoute.Source)
                {
                    dynRoute.Source = source;
                    dynRoute.CreatedBy = createdBy;
                }
            }
            
            // Update or add cluster in dynamic config - only add if doesn't exist
            var dynCluster = _dynamicConfig.Clusters.FirstOrDefault(c => 
                string.Equals(c.ClusterId, request.ClusterName, StringComparison.OrdinalIgnoreCase));
            
            if (dynCluster == null)
            {
                // Cluster doesn't exist in dynamic config - create new
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
            // else: Cluster exists - keep its existing destinations (don't modify)
            
            SaveDynamicConfig();

            var action = isNew ? "registered" : "updated";
            _logger.LogInformation("Route '{RouteName}' {Action} ({MatchPath} -> {Address})",
                request.RouteName, action, request.MatchPath, request.DestinationAddress);
            return new RouteOperationResult(true, $"Route '{request.RouteName}' {action}");
        }
    }

    /// <summary>
    /// Delete a route. Optionally removes the cluster if no remaining routes reference it.
    /// </summary>
    /// <param name="routeName">Route name to delete.</param>
    /// <param name="removeOrphanedCluster">If true, also remove the cluster when no other routes reference it. Default: true.</param>
    /// <returns>Route operation result.</returns>
    public RouteOperationResult TryRemoveRoute(string routeName, bool removeOrphanedCluster = true)
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

            var newClusters = (orphaned && removeOrphanedCluster)
                ? clusters.Where(c => !string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase)).ToList()
                : new List<ClusterConfig>(clusters);

            _configProvider.Update(newRoutes, newClusters);
            
            // Remove from dynamic config
            EnsureDynamicConfigInitialized();
            _dynamicConfig!.Routes.RemoveAll(r => 
                string.Equals(r.RouteId, routeName, StringComparison.OrdinalIgnoreCase));
            
            if (orphaned && removeOrphanedCluster && clusterId != null)
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
    /// Add or update a cluster independently (without creating a route).
    /// Thread-safe.
    /// </summary>
    /// <param name="clusterId">Cluster ID (unique identifier).</param>
    /// <param name="destinations">Destination addresses (name -> address mapping).</param>
    /// <param name="loadBalancingPolicy">Load balancing policy (optional).</param>
    /// <param name="healthCheck">Health check configuration (optional).</param>
    /// <param name="source">Configuration source: "dynamic" | "dashboard".</param>
    /// <param name="createdBy">Who created this cluster (optional).</param>
    /// <returns>Route operation result.</returns>
    public RouteOperationResult TryAddCluster(
        string clusterId,
        Dictionary<string, string> destinations,
        string? loadBalancingPolicy = null,
        Models.HealthCheckConfig? healthCheck = null,
        string source = "dynamic",
        string? createdBy = null)
    {
        if (string.IsNullOrWhiteSpace(clusterId))
            return new RouteOperationResult(false, "Cluster ID cannot be empty");

        if (destinations == null || destinations.Count == 0)
            return new RouteOperationResult(false, "At least one destination is required");

        lock (_lock)
        {
            var config = _configProvider.GetConfig();
            var routes = config.Routes?.ToList() ?? new List<RouteConfig>();
            var clusters = config.Clusters?.ToList() ?? new List<ClusterConfig>();
            var newClusters = new List<ClusterConfig>(clusters);

            var clusterConfig = new ClusterConfig
            {
                ClusterId = clusterId,
                Destinations = destinations.ToDictionary(
                    d => d.Key,
                    d => new DestinationConfig { Address = d.Value }),
                LoadBalancingPolicy = loadBalancingPolicy
            }; // Note: HealthCheck is set on the dynamic config but not directly on ClusterConfig (YARP limitation)

            var existingClusterIdx = clusters.FindIndex(c =>
                string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));

            bool isNew;
            if (existingClusterIdx >= 0)
            {
                newClusters[existingClusterIdx] = clusterConfig;
                isNew = false;
                _logger.LogInformation("Cluster '{ClusterId}' exists, updating", clusterId);
            }
            else
            {
                newClusters.Add(clusterConfig);
                isNew = true;
                _logger.LogInformation("Cluster '{ClusterId}' is new, adding", clusterId);
            }

            _configProvider.Update(routes, newClusters);

            // Save to dynamic config for persistence
            EnsureDynamicConfigInitialized();

            var dynCluster = _dynamicConfig!.Clusters.FirstOrDefault(c =>
                string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));

            if (dynCluster == null)
            {
                dynCluster = new DynamicClusterConfig
                {
                    ClusterId = clusterId,
                    Destinations = destinations,
                    LoadBalancingPolicy = loadBalancingPolicy,
                    HealthCheck = healthCheck,
                    Source = source,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = createdBy
                };
                _dynamicConfig.Clusters.Add(dynCluster);
            }
            else
            {
                dynCluster.Destinations = destinations;
                dynCluster.LoadBalancingPolicy = loadBalancingPolicy;
                dynCluster.HealthCheck = healthCheck;
                // Update source when modified via dashboard (so it won't be overwritten as "config" on restart)
                if (!string.IsNullOrEmpty(source) && source != dynCluster.Source)
                {
                    dynCluster.Source = source;
                    dynCluster.CreatedBy = createdBy;
                }
            }

            SaveDynamicConfig();

            var action = isNew ? "created" : "updated";
            _logger.LogInformation("Cluster '{ClusterId}' {Action} with {DestCount} destinations",
                clusterId, action, destinations.Count);
            return new RouteOperationResult(true, $"Cluster '{clusterId}' {action}");
        }
    }

    /// <summary>
    /// Get a specific cluster by ID.
    /// </summary>
    /// <param name="clusterId">Cluster ID to find.</param>
    /// <returns>Cluster configuration or null if not found.</returns>
    public ClusterConfig? GetCluster(string clusterId)
    {
        var clusters = GetClusters();
        return clusters.FirstOrDefault(c =>
            string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
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
    /// Rename a cluster: update all referencing routes, create new cluster, delete old cluster.
    /// All operations are performed atomically within a single lock.
    /// </summary>
    /// <param name="oldClusterId">Old cluster ID.</param>
    /// <param name="newClusterId">New cluster ID.</param>
    /// <param name="destinations">Destination addresses for the new cluster.</param>
    /// <param name="loadBalancingPolicy">Load balancing policy (optional).</param>
    /// <param name="healthCheck">Health check configuration (optional).</param>
    /// <param name="source">Configuration source.</param>
    /// <param name="createdBy">Who initiated the change.</param>
    /// <returns>Route operation result.</returns>
    public RouteOperationResult TryRenameCluster(
        string oldClusterId,
        string newClusterId,
        Dictionary<string, string> destinations,
        string? loadBalancingPolicy = null,
        Models.HealthCheckConfig? healthCheck = null,
        string source = "dashboard",
        string? createdBy = "dashboard-user")
    {
        if (string.IsNullOrWhiteSpace(oldClusterId) || string.IsNullOrWhiteSpace(newClusterId))
            return new RouteOperationResult(false, "Cluster ID cannot be empty");

        if (string.Equals(oldClusterId, newClusterId, StringComparison.OrdinalIgnoreCase))
            return new RouteOperationResult(false, "Old and new cluster IDs are the same");

        lock (_lock)
        {
            var config = _configProvider.GetConfig();
            var routes = config.Routes?.ToList() ?? new List<RouteConfig>();
            var clusters = config.Clusters?.ToList() ?? new List<ClusterConfig>();

            // Verify old cluster exists
            var oldCluster = clusters.FirstOrDefault(c =>
                string.Equals(c.ClusterId, oldClusterId, StringComparison.OrdinalIgnoreCase));
            if (oldCluster == null)
                return new RouteOperationResult(false, $"Cluster '{oldClusterId}' not found");

            // Check if new cluster ID already exists (and is not the old one)
            var newClusterExists = clusters.Any(c =>
                string.Equals(c.ClusterId, newClusterId, StringComparison.OrdinalIgnoreCase));
            if (newClusterExists)
                return new RouteOperationResult(false, $"Cluster '{newClusterId}' already exists");

            // Step 1: Create new cluster with new ID
            var newCluster = new ClusterConfig
            {
                ClusterId = newClusterId,
                Destinations = destinations.ToDictionary(d => d.Key, d => new DestinationConfig { Address = d.Value }),
                LoadBalancingPolicy = loadBalancingPolicy
            };
            var newClusters = new List<ClusterConfig>(clusters) { newCluster };

            // Step 2: Update all routes that reference the old cluster to point to the new one
            var newRoutes = new List<RouteConfig>(routes);
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

            // Step 3: Remove old cluster
            newClusters = newClusters.Where(c =>
                !string.Equals(c.ClusterId, oldClusterId, StringComparison.OrdinalIgnoreCase)).ToList();

            // Apply all changes in a single update
            _configProvider.Update(newRoutes, newClusters);

            // Update dynamic config
            EnsureDynamicConfigInitialized();

            // Add new cluster to dynamic config
            _dynamicConfig!.Clusters.Add(new DynamicClusterConfig
            {
                ClusterId = newClusterId,
                Destinations = destinations,
                LoadBalancingPolicy = loadBalancingPolicy,
                HealthCheck = healthCheck,
                Source = source,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            });

            // Update referencing routes in dynamic config
            foreach (var dynRoute in _dynamicConfig.Routes.Where(r =>
                string.Equals(r.ClusterId, oldClusterId, StringComparison.OrdinalIgnoreCase)))
            {
                dynRoute.ClusterId = newClusterId;
            }

            // Remove old cluster from dynamic config
            _dynamicConfig.Clusters.RemoveAll(c =>
                string.Equals(c.ClusterId, oldClusterId, StringComparison.OrdinalIgnoreCase));

            SaveDynamicConfig();

            _logger.LogInformation(
                "Cluster '{OldClusterId}' renamed to '{NewClusterId}', updated {RouteCount} referencing routes",
                oldClusterId, newClusterId, updatedRouteCount);
            return new RouteOperationResult(true,
                $"Cluster '{oldClusterId}' renamed to '{newClusterId}', {updatedRouteCount} route(s) updated");
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
    /// Add a new cluster independently.
    /// </summary>
    /// <param name="request">Cluster creation request.</param>
    /// <param name="source">Configuration source: "dynamic" | "auto-register".</param>
    /// <param name="createdBy">Who created this cluster (optional).</param>
    /// <returns>Route operation result.</returns>
    public RouteOperationResult TryAddCluster(
        CreateClusterRequest request,
        string source = "dynamic",
        string? createdBy = null)
    {
        lock (_lock)
        {
            var config = _configProvider.GetConfig();
            var clusters = config.Clusters?.ToList() ?? new List<ClusterConfig>();
            
            // Check if cluster already exists
            if (clusters.Any(c => string.Equals(c.ClusterId, request.ClusterId, StringComparison.OrdinalIgnoreCase)))
            {
                return new RouteOperationResult(false, $"Cluster '{request.ClusterId}' already exists. Use update instead.");
            }

            var clusterConfig = new ClusterConfig
            {
                ClusterId = request.ClusterId,
                Destinations = request.Destinations.ToDictionary(
                    d => d.Key,
                    d => new DestinationConfig { Address = d.Value }),
                LoadBalancingPolicy = request.LoadBalancingPolicy
            };

            clusters.Add(clusterConfig);
            _configProvider.Update(config.Routes ?? Array.Empty<RouteConfig>(), clusters);

            // Save to dynamic config
            EnsureDynamicConfigInitialized();
            _dynamicConfig!.Clusters.Add(new DynamicClusterConfig
            {
                ClusterId = request.ClusterId,
                Destinations = request.Destinations,
                LoadBalancingPolicy = request.LoadBalancingPolicy,
                HealthCheck = request.HealthCheck != null ? new Aneiang.Yarp.Models.HealthCheckConfig
                {
                    Active = request.HealthCheck.Active?.Enabled ?? false,
                    Endpoint = request.HealthCheck.Active?.Path
                } : null,
                Source = source,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            });

            SaveDynamicConfig();
            _logger.LogInformation("Cluster '{ClusterId}' created with {DestCount} destinations", 
                request.ClusterId, request.Destinations.Count);
            return new RouteOperationResult(true, $"Cluster '{request.ClusterId}' created successfully");
        }
    }

    /// <summary>
    /// Update an existing cluster.
    /// </summary>
    /// <param name="clusterId">Cluster ID to update.</param>
    /// <param name="request">Cluster update request.</param>
    /// <returns>Route operation result.</returns>
    public RouteOperationResult TryUpdateCluster(string clusterId, UpdateClusterRequest request)
    {
        if (string.IsNullOrWhiteSpace(clusterId))
            return new RouteOperationResult(false, "Cluster ID cannot be empty");

        lock (_lock)
        {
            var config = _configProvider.GetConfig();
            var clusters = config.Clusters?.ToList() ?? new List<ClusterConfig>();
            
            var existingIdx = clusters.FindIndex(c => 
                string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
            
            if (existingIdx < 0)
            {
                return new RouteOperationResult(false, $"Cluster '{clusterId}' not found");
            }

            var existing = clusters[existingIdx];
            var updated = new ClusterConfig
            {
                ClusterId = existing.ClusterId,
                Destinations = request.Destinations?.ToDictionary(
                    d => d.Key,
                    d => new DestinationConfig { Address = d.Value }) ?? existing.Destinations,
                LoadBalancingPolicy = request.LoadBalancingPolicy ?? existing.LoadBalancingPolicy
            };

            clusters[existingIdx] = updated;
            _configProvider.Update(config.Routes ?? Array.Empty<RouteConfig>(), clusters);

            // Update dynamic config
            EnsureDynamicConfigInitialized();
            var dynCluster = _dynamicConfig!.Clusters.FirstOrDefault(c => 
                string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
            
            if (dynCluster != null)
            {
                if (request.Destinations != null) dynCluster.Destinations = request.Destinations;
                if (request.LoadBalancingPolicy != null) dynCluster.LoadBalancingPolicy = request.LoadBalancingPolicy;
                if (request.HealthCheck != null)
                {
                    dynCluster.HealthCheck = new Aneiang.Yarp.Models.HealthCheckConfig
                    {
                        Active = request.HealthCheck.Active?.Enabled ?? false,
                        Endpoint = request.HealthCheck.Active?.Path
                    };
                }
            }

            SaveDynamicConfig();
            _logger.LogInformation("Cluster '{ClusterId}' updated", clusterId);
            return new RouteOperationResult(true, $"Cluster '{clusterId}' updated successfully");
        }
    }
    
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

    /// <summary>
    /// Replace entire configuration in one batch operation.
    /// This is used for rollback operations to avoid multiple file saves.
    /// </summary>
    /// <param name="newRoutes">New routes to set.</param>
    /// <param name="newClusters">New clusters to set.</param>
    /// <param name="source">Source of the change (e.g., "rollback").</param>
    /// <param name="createdBy">Who initiated the change.</param>
    public void ReplaceAllConfig(
        IReadOnlyList<RouteConfig> newRoutes,
        IReadOnlyList<ClusterConfig> newClusters,
        string source = "rollback",
        string? createdBy = "dashboard-user")
    {
        lock (_lock)
        {
            // Update YARP in-memory config in one operation
            _configProvider.Update(newRoutes, newClusters);
            
            // Update dynamic config metadata
            EnsureDynamicConfigInitialized();
            _dynamicConfig!.Routes.Clear();
            _dynamicConfig.Clusters.Clear();
            
            foreach (var cluster in newClusters)
            {
                var dynCluster = new DynamicClusterConfig
                {
                    ClusterId = cluster.ClusterId,
                    Destinations = cluster.Destinations?.ToDictionary(
                        d => d.Key,
                        d => d.Value.Address ?? string.Empty) ?? new Dictionary<string, string>(),
                    LoadBalancingPolicy = cluster.LoadBalancingPolicy,
                    HealthCheck = null,
                    Source = source,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = createdBy
                }; 
                _dynamicConfig.Clusters.Add(dynCluster);
            }
            
            foreach (var route in newRoutes)
            {
                var dynRoute = new DynamicRouteConfig
                {
                    RouteId = route.RouteId ?? string.Empty,
                    ClusterId = route.ClusterId ?? string.Empty,
                    MatchPath = route.Match?.Path ?? string.Empty,
                    Order = route.Order ?? 50,
                    Transforms = route.Transforms?.Select(t => new Dictionary<string, string>(t)).ToList(),
                    Source = source,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = createdBy
                }; 
                _dynamicConfig.Routes.Add(dynRoute);
            }
            
            // Save to file only once at the end
            SaveDynamicConfig();
            
            _logger.LogInformation(
                "Configuration replaced: {Routes} routes, {Clusters} clusters",
                newRoutes.Count,
                newClusters.Count);
        }
    }
}
