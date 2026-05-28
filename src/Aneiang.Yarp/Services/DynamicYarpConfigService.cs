using System.Threading;
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
    private readonly ConfigChangeAuditLog _auditLog;
    private readonly ILogger<DynamicYarpConfigService> _logger;
    private readonly ReaderWriterLockSlim _rwLock = new();
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
    /// <param name="auditLog">Config change audit log service.</param>
    /// <param name="logger">Logger instance.</param>
    public DynamicYarpConfigService(
        InMemoryConfigProvider configProvider,
        DynamicConfigPersistenceService persistence,
        ConfigChangeAuditLog auditLog,
        ILogger<DynamicYarpConfigService> logger)
    {
        _configProvider = configProvider;
        _persistence = persistence;
        _auditLog = auditLog;
        _logger = logger;

        // Load dynamic config from file on startup (sync is fine for one-time init)
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
                    _staticRouteIds.Add(route.RouteId ?? string.Empty);

                    var existingRoute = _dynamicConfig!.Routes.FirstOrDefault(r =>
                        string.Equals(r.RouteId, route.RouteId, StringComparison.OrdinalIgnoreCase));

                    if (existingRoute == null)
                    {
                        _dynamicConfig.Routes.Add(new DynamicRouteConfig
                        {
                            RouteId = route.RouteId ?? string.Empty,
                            ClusterId = route.ClusterId ?? string.Empty,
                            MatchPath = route.Match?.Path!,
                            Order = route.Order ?? 50,
                            Transforms = route.Transforms?.Select(t => new Dictionary<string, string>(t)).ToList(),
                            Source = "config",
                            CreatedAt = DateTime.UtcNow,
                            CreatedBy = "appsettings.json"
                        });
                    }
                }
            }

            // Record static clusters
            if (currentConfig.Clusters != null)
            {
                foreach (var cluster in currentConfig.Clusters)
                {
                    _staticClusterIds.Add(cluster.ClusterId ?? string.Empty);

                    var existingCluster = _dynamicConfig!.Clusters.FirstOrDefault(c =>
                        string.Equals(c.ClusterId, cluster.ClusterId, StringComparison.OrdinalIgnoreCase));

                    if (existingCluster == null)
                    {
                        var destinations = cluster.Destinations?.ToDictionary(
                            d => d.Key,
                            d => d.Value.Address ?? string.Empty) ?? new Dictionary<string, string>();

                        _dynamicConfig.Clusters.Add(new DynamicClusterConfig
                        {
                            ClusterId = cluster.ClusterId ?? string.Empty,
                            Destinations = destinations,
                            LoadBalancingPolicy = cluster.LoadBalancingPolicy ?? string.Empty,
                            Source = "config",
                            CreatedAt = DateTime.UtcNow,
                            CreatedBy = "appsettings.json"
                        });
                    }
                }
            }

            // Save the merged config (sync is fine for one-time startup)
            _persistence.SaveConfig(_dynamicConfig!);

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
        var routes = new List<RouteConfig>(currentConfig.Routes ?? []);
        var clusters = new List<ClusterConfig>(currentConfig.Clusters ?? []);

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
                LoadBalancingPolicy = dynCluster.LoadBalancingPolicy,
                HealthCheck = BuildClusterHealthCheck(dynCluster.HealthCheck)
            };

            if (existingIdx >= 0)
                clusters[existingIdx] = clusterConfig;
            else
                clusters.Add(clusterConfig);
        }

        _configProvider.Update(routes, SanitizeClusters(clusters));
    }

    /// <summary>
    /// Save dynamic configuration to persistence file synchronously (startup only).
    /// </summary>
    internal void SaveDynamicConfigSync()
    {
        if (_dynamicConfig == null)
            _dynamicConfig = new GatewayDynamicConfig();

        _persistence.SaveConfig(_dynamicConfig);
    }

    /// <summary>
    /// Save dynamic configuration to persistence file asynchronously (runtime operations).
    /// Uses async I/O to avoid blocking threads during file writes.
    /// </summary>
    private async Task SaveDynamicConfigAsync()
    {
        if (_dynamicConfig == null)
            _dynamicConfig = new GatewayDynamicConfig();

        await _persistence.SaveConfigAsync(_dynamicConfig);
    }

    /// <summary>
    /// Sanitize cluster list by removing destinations with empty/null addresses.
    /// YARP rejects the entire config update if any destination has an invalid address.
    /// </summary>
    private static IReadOnlyList<ClusterConfig> SanitizeClusters(IReadOnlyList<ClusterConfig> clusters)
    {
        var sanitized = new List<ClusterConfig>();
        foreach (var cluster in clusters)
        {
            if (cluster.Destinations == null || cluster.Destinations.Count == 0)
            {
                // Keep cluster even with no destinations (YARP allows it)
                sanitized.Add(cluster);
                continue;
            }

            var validDests = new Dictionary<string, DestinationConfig>(
                cluster.Destinations.Where(d =>
                    !string.IsNullOrWhiteSpace(d.Value?.Address)));

            if (validDests.Count < cluster.Destinations.Count)
            {
                _ = cluster; // Log if needed
            }

            sanitized.Add(new ClusterConfig
            {
                ClusterId = cluster.ClusterId,
                Destinations = validDests,
                LoadBalancingPolicy = cluster.LoadBalancingPolicy,
                HttpClient = cluster.HttpClient,
                HttpRequest = cluster.HttpRequest,
                Metadata = cluster.Metadata,
                HealthCheck = cluster.HealthCheck,
                SessionAffinity = cluster.SessionAffinity
            });
        }

        return sanitized;
    }

    /// <summary>
    /// Add or update a route (creates or replaces cluster). Thread-safe.
    /// </summary>
    /// <param name="request">Route registration request.</param>
    /// <param name="source">Configuration source: "dynamic" | "auto-register".</param>
    /// <param name="createdBy">Who created this route (optional).</param>
    /// <returns>Route operation result.</returns>
    public async Task<RouteOperationResult> TryAddRoute(
        RegisterRouteRequest request,
        string source = "dynamic",
        string? createdBy = null)
    {
        bool saveNeeded = false;
        _rwLock.EnterWriteLock();
        try
        {
            var config = _configProvider.GetConfig();
            // Single copy: use newRoutes/newClusters for both lookup and modification (#6)
            var newRoutes = new List<RouteConfig>(config.Routes ?? []);
            var newClusters = new List<ClusterConfig>(config.Clusters ?? []);

            // -- Route: create or update
            var routeConfig = new RouteConfig
            {
                RouteId = request.RouteName,
                ClusterId = request.ClusterName,
                Match = new RouteMatch { Path = request.MatchPath },
                Order = request.Order ?? 50,
                Transforms = request.Transforms?.Select(t => (IReadOnlyDictionary<string, string>)t).ToList()
            };

            var existingRouteIdx = newRoutes.FindIndex(r =>
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

            // -- Cluster: create or update
            if (request.UseIpIsolation && !string.IsNullOrWhiteSpace(request.ClientIp))
            {
                // IP isolation: add destination with ClientIp metadata to shared cluster
                var destKey = $"ip-{request.ClientIp.Replace(".", "-")}";
                var existingClusterIdx = newClusters.FindIndex(c =>
                    string.Equals(c.ClusterId, request.ClusterName, StringComparison.OrdinalIgnoreCase));

                if (existingClusterIdx >= 0)
                {
                    var existingCluster = newClusters[existingClusterIdx];
                    var destinations = existingCluster.Destinations?.ToDictionary(
                        d => d.Key, d => d.Value) ?? new Dictionary<string, DestinationConfig>();

                    destinations[destKey] = new DestinationConfig
                    {
                        Address = request.DestinationAddress,
                        Metadata = new Dictionary<string, string> { { "ClientIp", request.ClientIp } }
                    };

                    newClusters[existingClusterIdx] = new ClusterConfig
                    {
                        ClusterId = request.ClusterName,
                        Destinations = destinations,
                        LoadBalancingPolicy = existingCluster.LoadBalancingPolicy ?? "IpBased",
                        HealthCheck = existingCluster.HealthCheck
                    };
                }
                else
                {
                    newClusters.Add(new ClusterConfig
                    {
                        ClusterId = request.ClusterName,
                        Destinations = new Dictionary<string, DestinationConfig>
                        {
                            [destKey] = new DestinationConfig
                            {
                                Address = request.DestinationAddress,
                                Metadata = new Dictionary<string, string> { { "ClientIp", request.ClientIp } }
                            }
                        },
                        LoadBalancingPolicy = "IpBased",
                        HealthCheck = BuildClusterHealthCheck(null)
                    });
                }

                _logger.LogInformation(
                    "IP isolation enabled for route '{RouteName}'. Client IP: {ClientIp}, Destination: {Dest}",
                    request.RouteName, request.ClientIp, request.DestinationAddress);
            }
            else
            {
                // Normal: create/replace cluster with single destination
                var existingClusterIdx = newClusters.FindIndex(c =>
                    string.Equals(c.ClusterId, request.ClusterName, StringComparison.OrdinalIgnoreCase));

                if (existingClusterIdx >= 0)
                {
                    // Cluster already exists - only update if we have a valid destination address
                    if (!string.IsNullOrWhiteSpace(request.DestinationAddress))
                    {
                        var existingCluster = newClusters[existingClusterIdx];
                        newClusters[existingClusterIdx] = new ClusterConfig
                        {
                            ClusterId = request.ClusterName,
                            Destinations = new Dictionary<string, DestinationConfig>
                            {
                                ["d1"] = new() { Address = request.DestinationAddress }
                            },
                            HealthCheck = existingCluster.HealthCheck
                        };
                    }
                    // else: keep the existing cluster unchanged
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(request.DestinationAddress))
                    {
                        _auditLog.RecordFailure("AddRoute", request.RouteName,
                            "Destination address is required when the cluster does not exist", createdBy);
                        return new RouteOperationResult(false,
                            "Destination address is required when the cluster does not exist");
                    }

                    newClusters.Add(new ClusterConfig
                    {
                        ClusterId = request.ClusterName,
                        Destinations = new Dictionary<string, DestinationConfig>
                        {
                            ["d1"] = new() { Address = request.DestinationAddress }
                        },
                        HealthCheck = null
                    });
                }
            }

            _configProvider.Update(newRoutes, SanitizeClusters(newClusters));

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
                if (!string.IsNullOrEmpty(source) && source != dynRoute.Source)
                {
                    dynRoute.Source = source;
                    dynRoute.CreatedBy = createdBy;
                }
            }

            // Update or add cluster in dynamic config
            var dynCluster = _dynamicConfig.Clusters.FirstOrDefault(c =>
                string.Equals(c.ClusterId, request.ClusterName, StringComparison.OrdinalIgnoreCase));

            if (dynCluster == null)
            {
                if (request.UseIpIsolation && !string.IsNullOrWhiteSpace(request.ClientIp))
                {
                    var destKey = $"ip-{request.ClientIp.Replace(".", "-")}";
                    dynCluster = new DynamicClusterConfig
                    {
                        ClusterId = request.ClusterName,
                        Destinations = new Dictionary<string, string> { [destKey] = request.DestinationAddress },
                        LoadBalancingPolicy = "IpBased",
                        Source = source,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = createdBy
                    };
                }
                else
                {
                    dynCluster = new DynamicClusterConfig
                    {
                        ClusterId = request.ClusterName,
                        Destinations = new Dictionary<string, string> { ["d1"] = request.DestinationAddress },
                        Source = source,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = createdBy
                    };
                }
                _dynamicConfig.Clusters.Add(dynCluster);
            }
            else if (!string.IsNullOrWhiteSpace(request.DestinationAddress) && !request.UseIpIsolation)
            {
                // Update existing cluster destination only when a valid address is provided
                dynCluster.Destinations["d1"] = request.DestinationAddress;
            }

            saveNeeded = true;

            var action = isNew ? "registered" : "updated";
            _logger.LogInformation("Route '{RouteName}' {Action} ({MatchPath} -> {Address})",
                request.RouteName, action, request.MatchPath, request.DestinationAddress);
            _auditLog.RecordSuccess(
                isNew ? "AddRoute" : "UpdateRoute",
                request.RouteName,
                createdBy, null,
                new { clusterId = request.ClusterName, matchPath = request.MatchPath },
                new { clusterId = request.ClusterName, matchPath = request.MatchPath, destination = request.DestinationAddress });
            return new RouteOperationResult(true, $"Route '{request.RouteName}' {action}");
        }
        finally
        {
            _rwLock.ExitWriteLock();
            if (saveNeeded)
                await SaveDynamicConfigAsync();
        }
    }

    /// <summary>
    /// Delete a route. Optionally removes the cluster if no remaining routes reference it.
    /// </summary>
    /// <param name="routeName">Route name to delete.</param>
    /// <param name="clientIp">Optional client IP for IP-based isolation: only removes the matching destination instead of the whole cluster.</param>
    /// <param name="removeOrphanedCluster">If true, also remove the cluster when no other routes reference it. Default: true.</param>
    /// <returns>Route operation result.</returns>
    public async Task<RouteOperationResult> TryRemoveRoute(string routeName, string? clientIp = null, bool removeOrphanedCluster = true)
    {
        if (string.IsNullOrWhiteSpace(routeName))
            return new RouteOperationResult(false, "Route name cannot be empty");

        bool saveNeeded = false;
        _rwLock.EnterWriteLock();
        try
        {
            var config = _configProvider.GetConfig();
            // Use IReadOnlyList directly for read-only lookups (#6)
            var routes = config.Routes ?? Array.Empty<RouteConfig>();
            var clusters = config.Clusters ?? Array.Empty<ClusterConfig>();

            var route = routes.FirstOrDefault(r =>
                string.Equals(r.RouteId, routeName, StringComparison.OrdinalIgnoreCase));
            if (route == null)
            {
                _auditLog.RecordFailure("RemoveRoute", routeName, $"Route '{routeName}' not found");
                return new RouteOperationResult(false, $"Route '{routeName}' not found");
            }

            var clusterId = route.ClusterId;

            // IP isolation: only remove the matching destination, keep the route and cluster
            if (!string.IsNullOrWhiteSpace(clientIp) && clusterId != null)
            {
                var destKey = $"ip-{clientIp.Replace(".", "-")}";
                var cluster = clusters.FirstOrDefault(c =>
                    string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));

                if (cluster != null)
                {
                    var destinations = cluster.Destinations?.ToDictionary(
                        d => d.Key, d => d.Value) ?? new Dictionary<string, DestinationConfig>();

                    destinations.Remove(destKey);

                    // Materialize as List for FindIndex and in-place modification (#6)
                    var mutableClusters = new List<ClusterConfig>(clusters);
                    var clusterIdx = mutableClusters.FindIndex(c =>
                        string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));

                    if (clusterIdx >= 0)
                    {
                        if (destinations.Count == 0)
                        {
                            // No more destinations, remove cluster and route
                            mutableClusters.RemoveAt(clusterIdx);
                            var newRoutes = new List<RouteConfig>(routes.Where(r =>
                                !string.Equals(r.RouteId, routeName, StringComparison.OrdinalIgnoreCase)));
                            _configProvider.Update(newRoutes, SanitizeClusters(mutableClusters));

                            EnsureDynamicConfigInitialized();
                            _dynamicConfig!.Routes.RemoveAll(r =>
                                string.Equals(r.RouteId, routeName, StringComparison.OrdinalIgnoreCase));
                            _dynamicConfig.Clusters.RemoveAll(c =>
                                string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
                        }
                        else
                        {
                            // Update cluster without the removed destination
                            mutableClusters[clusterIdx] = new ClusterConfig
                            {
                                ClusterId = cluster.ClusterId,
                                Destinations = destinations,
                                LoadBalancingPolicy = cluster.LoadBalancingPolicy,
                                Metadata = cluster.Metadata
                            };
                            _configProvider.Update(routes, SanitizeClusters(mutableClusters));

                            EnsureDynamicConfigInitialized();
                            var dynCluster = _dynamicConfig!.Clusters.FirstOrDefault(c =>
                                string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
                            if (dynCluster != null)
                            {
                                dynCluster.Destinations.Remove(destKey);
                            }
                        }

                        saveNeeded = true;
                        _logger.LogInformation(
                            "IP isolation: removed destination '{DestKey}' from cluster '{ClusterId}' (client IP: {ClientIp})",
                            destKey, clusterId, clientIp);
                        return new RouteOperationResult(true,
                            $"Destination for IP '{clientIp}' removed from cluster '{clusterId}'");
                    }
                }

                return new RouteOperationResult(false, $"Cluster '{clusterId}' not found");
            }

            // Normal: delete route and optionally the orphaned cluster
            var newRoutes2 = new List<RouteConfig>(routes.Where(r =>
                !string.Equals(r.RouteId, routeName, StringComparison.OrdinalIgnoreCase)));

            var orphaned = clusterId != null && !newRoutes2.Any(r =>
                string.Equals(r.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));

            IReadOnlyList<ClusterConfig> newClusters2;
            if (orphaned && removeOrphanedCluster && clusterId != null)
            {
                newClusters2 = new List<ClusterConfig>(clusters.Where(c =>
                    !string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                newClusters2 = clusters;
            }

            _configProvider.Update(newRoutes2, SanitizeClusters(newClusters2));

            // Remove from dynamic config
            EnsureDynamicConfigInitialized();
            _dynamicConfig!.Routes.RemoveAll(r =>
                string.Equals(r.RouteId, routeName, StringComparison.OrdinalIgnoreCase));

            if (orphaned && removeOrphanedCluster && clusterId != null)
            {
                _dynamicConfig.Clusters.RemoveAll(c =>
                    string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
            }

            saveNeeded = true;

            _logger.LogInformation("Route '{RouteName}' deleted", routeName);
            _auditLog.RecordSuccess("RemoveRoute", routeName, null, null,
                new { clusterId, matchPath = route.Match?.Path });
            return new RouteOperationResult(true, $"Route '{routeName}' deleted");
        }
        finally
        {
            _rwLock.ExitWriteLock();
            if (saveNeeded)
                await SaveDynamicConfigAsync();
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
    public async Task<RouteOperationResult> TryAddCluster(
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
        {
            _auditLog.RecordFailure("AddCluster", clusterId ?? "", "At least one destination is required");
            return new RouteOperationResult(false, "At least one destination is required");
        }

        bool saveNeeded = false;
        _rwLock.EnterWriteLock();
        try
        {
            var config = _configProvider.GetConfig();
            // Routes are not modified — pass IReadOnlyList directly (#6)
            var routes = config.Routes ?? Array.Empty<RouteConfig>();
            var newClusters = new List<ClusterConfig>(config.Clusters ?? []);

            var clusterConfig = new ClusterConfig
            {
                ClusterId = clusterId,
                Destinations = destinations.ToDictionary(
                    d => d.Key,
                    d => new DestinationConfig { Address = d.Value }),
                LoadBalancingPolicy = loadBalancingPolicy,
                HealthCheck = BuildClusterHealthCheck(healthCheck)
            };

            var existingClusterIdx = newClusters.FindIndex(c =>
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

            _configProvider.Update(routes, SanitizeClusters(newClusters));

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
                if (!string.IsNullOrEmpty(source) && source != dynCluster.Source)
                {
                    dynCluster.Source = source;
                    dynCluster.CreatedBy = createdBy;
                }
            }

            saveNeeded = true;

            var action = isNew ? "created" : "updated";
            _logger.LogInformation("Cluster '{ClusterId}' {Action} with {DestCount} destinations",
                clusterId, action, destinations.Count);
            _auditLog.RecordSuccess(
                isNew ? "AddCluster" : "UpdateCluster",
                clusterId, createdBy, null,
                null,
                new { destinations, loadBalancingPolicy });
            return new RouteOperationResult(true, $"Cluster '{clusterId}' {action}");
        }
        finally
        {
            _rwLock.ExitWriteLock();
            if (saveNeeded)
                await SaveDynamicConfigAsync();
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
    public async Task<RouteOperationResult> TryRemoveCluster(string clusterId)
    {
        if (string.IsNullOrWhiteSpace(clusterId))
            return new RouteOperationResult(false, "Cluster ID cannot be empty");

        bool saveNeeded = false;
        _rwLock.EnterWriteLock();
        try
        {
            var config = _configProvider.GetConfig();
            // Routes are not modified — use IReadOnlyList directly (#6)
            var routes = config.Routes ?? Array.Empty<RouteConfig>();
            var clusters = config.Clusters ?? Array.Empty<ClusterConfig>();

            // Check if any routes reference this cluster
            var hasReferencingRoutes = routes.Any(r =>
                string.Equals(r.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));

            if (hasReferencingRoutes)
            {
                _auditLog.RecordFailure("RemoveCluster", clusterId,
                    $"Cluster '{clusterId}' is referenced by route(s)");
                return new RouteOperationResult(false,
                    $"Cluster '{clusterId}' is referenced by route(s). Delete routes first.");
            }

            var cluster = clusters.FirstOrDefault(c =>
                string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
            if (cluster == null)
            {
                _auditLog.RecordFailure("RemoveCluster", clusterId, $"Cluster '{clusterId}' not found");
                return new RouteOperationResult(false, $"Cluster '{clusterId}' not found");
            }

            var newClusters = new List<ClusterConfig>(clusters.Where(c =>
                !string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase)));

            _configProvider.Update(routes, SanitizeClusters(newClusters));

            // Remove from dynamic config
            EnsureDynamicConfigInitialized();
            _dynamicConfig!.Clusters.RemoveAll(c =>
                string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));

            saveNeeded = true;

            _logger.LogInformation("Cluster '{ClusterId}' deleted", clusterId);
            _auditLog.RecordSuccess("RemoveCluster", clusterId, null, null,
                new { destinations = cluster.Destinations?.Count });
            return new RouteOperationResult(true, $"Cluster '{clusterId}' deleted");
        }
        finally
        {
            _rwLock.ExitWriteLock();
            if (saveNeeded)
                await SaveDynamicConfigAsync();
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
    public async Task<RouteOperationResult> TryRenameCluster(
        string oldClusterId,
        string newClusterId,
        Dictionary<string, string> destinations,
        string? loadBalancingPolicy = null,
        Models.HealthCheckConfig? healthCheck = null,
        string source = "dashboard",
        string? createdBy = "dashboard-user")
    {
        if (string.IsNullOrWhiteSpace(oldClusterId) || string.IsNullOrWhiteSpace(newClusterId))
        {
            _auditLog.RecordFailure("RenameCluster", oldClusterId ?? "", "Cluster ID cannot be empty");
            return new RouteOperationResult(false, "Cluster ID cannot be empty");
        }

        if (string.Equals(oldClusterId, newClusterId, StringComparison.OrdinalIgnoreCase))
        {
            _auditLog.RecordFailure("RenameCluster", oldClusterId, "Old and new cluster IDs are the same");
            return new RouteOperationResult(false, "Old and new cluster IDs are the same");
        }

        bool saveNeeded = false;
        _rwLock.EnterWriteLock();
        try
        {
            var config = _configProvider.GetConfig();
            // Single copy for both lookup and modification (#6)
            var newRoutes = new List<RouteConfig>(config.Routes ?? []);
            var newClusters = new List<ClusterConfig>(config.Clusters ?? []);

            // Verify old cluster exists
            var oldCluster = newClusters.FirstOrDefault(c =>
                string.Equals(c.ClusterId, oldClusterId, StringComparison.OrdinalIgnoreCase));
            if (oldCluster == null)
            {
                _auditLog.RecordFailure("RenameCluster", oldClusterId, $"Cluster '{oldClusterId}' not found");
                return new RouteOperationResult(false, $"Cluster '{oldClusterId}' not found");
            }

            // Check if new cluster ID already exists (and is not the old one)
            if (newClusters.Any(c =>
                string.Equals(c.ClusterId, newClusterId, StringComparison.OrdinalIgnoreCase)))
            {
                _auditLog.RecordFailure("RenameCluster", oldClusterId, $"Cluster '{newClusterId}' already exists");
                return new RouteOperationResult(false, $"Cluster '{newClusterId}' already exists");
            }

            // Step 1: Create new cluster with new ID
            var newCluster = new ClusterConfig
            {
                ClusterId = newClusterId,
                Destinations = destinations.ToDictionary(d => d.Key, d => new DestinationConfig { Address = d.Value }),
                LoadBalancingPolicy = loadBalancingPolicy,
                HealthCheck = BuildClusterHealthCheck(healthCheck)
            };
            newClusters.Add(newCluster);

            // Step 2: Update all routes that reference the old cluster to point to the new one
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
            newClusters.RemoveAll(c =>
                string.Equals(c.ClusterId, oldClusterId, StringComparison.OrdinalIgnoreCase));

            // Apply all changes in a single update
            _configProvider.Update(newRoutes, SanitizeClusters(newClusters));

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

            saveNeeded = true;

            _logger.LogInformation(
                "Cluster '{OldClusterId}' renamed to '{NewClusterId}', updated {RouteCount} referencing routes",
                oldClusterId, newClusterId, updatedRouteCount);
            _auditLog.RecordSuccess("RenameCluster", $"{oldClusterId} → {newClusterId}", createdBy, null,
                new { oldClusterId, routesUpdated = updatedRouteCount },
                new { newClusterId, destinations, loadBalancingPolicy });
            return new RouteOperationResult(true,
                $"Cluster '{oldClusterId}' renamed to '{newClusterId}', {updatedRouteCount} route(s) updated");
        }
        finally
        {
            _rwLock.ExitWriteLock();
            if (saveNeeded)
                await SaveDynamicConfigAsync();
        }
    }

    /// <summary>
    /// Get all routes from the current YARP configuration.
    /// </summary>
    /// <returns>Read-only list of route configurations.</returns>
    public IReadOnlyList<RouteConfig> GetRoutes()
    {
        _rwLock.EnterReadLock();
        try
        {
            return _configProvider.GetConfig().Routes ?? Array.Empty<RouteConfig>();
        }
        finally { _rwLock.ExitReadLock(); }
    }

    /// <summary>
    /// Get all clusters from the current YARP configuration.
    /// </summary>
    /// <returns>Read-only list of cluster configurations.</returns>
    public IReadOnlyList<ClusterConfig> GetClusters()
    {
        _rwLock.EnterReadLock();
        try
        {
            return _configProvider.GetConfig().Clusters ?? Array.Empty<ClusterConfig>();
        }
        finally { _rwLock.ExitReadLock(); }
    }

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
    public async Task<RouteOperationResult> TryAddCluster(
        CreateClusterRequest request,
        string source = "dynamic",
        string? createdBy = null)
    {
        bool saveNeeded = false;
        _rwLock.EnterWriteLock();
        try
        {
            var config = _configProvider.GetConfig();
            var clusters = config.Clusters ?? Array.Empty<ClusterConfig>();

            // Check if cluster already exists
            if (clusters.Any(c => string.Equals(c.ClusterId, request.ClusterId, StringComparison.OrdinalIgnoreCase)))
            {
                _auditLog.RecordFailure("AddCluster", request.ClusterId,
                    $"Cluster '{request.ClusterId}' already exists", createdBy);
                return new RouteOperationResult(false, $"Cluster '{request.ClusterId}' already exists. Use update instead.");
            }

            var newClusters = new List<ClusterConfig>(clusters);
            var clusterConfig = new ClusterConfig
            {
                ClusterId = request.ClusterId,
                Destinations = request.Destinations.ToDictionary(
                    d => d.Key,
                    d => new DestinationConfig { Address = d.Value }),
                LoadBalancingPolicy = request.LoadBalancingPolicy,
                HealthCheck = BuildClusterHealthCheck(request.HealthCheck != null ? new Models.HealthCheckConfig
                {
                    Active = request.HealthCheck.Active?.Enabled ?? false,
                    Endpoint = request.HealthCheck.Active?.Path,
                    Passive = request.HealthCheck.Passive?.Enabled ?? false,
                    PassivePolicy = request.HealthCheck.Passive?.Policy,
                    PassiveReactivationPeriod = TimeSpan.TryParse(request.HealthCheck.Passive?.ReactivationPeriod, out var rp) ? rp : TimeSpan.FromSeconds(30),
                    AvailableDestinationsPolicy = request.HealthCheck.AvailableDestinationsPolicy
                } : null)
            };

            newClusters.Add(clusterConfig);
            _configProvider.Update(config.Routes ?? Array.Empty<RouteConfig>(), SanitizeClusters(newClusters));

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
                    Endpoint = request.HealthCheck.Active?.Path,
                    Passive = request.HealthCheck.Passive?.Enabled ?? false,
                    PassivePolicy = request.HealthCheck.Passive?.Policy,
                    PassiveReactivationPeriod = TimeSpan.TryParse(request.HealthCheck.Passive?.ReactivationPeriod, out var rp2) ? rp2 : TimeSpan.FromSeconds(30),
                    AvailableDestinationsPolicy = request.HealthCheck.AvailableDestinationsPolicy
                } : null,
                Source = source,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            });

            saveNeeded = true;
            _logger.LogInformation("Cluster '{ClusterId}' created with {DestCount} destinations",
                request.ClusterId, request.Destinations.Count);
            _auditLog.RecordSuccess("AddCluster", request.ClusterId, createdBy, null, null,
                new { destinations = request.Destinations, loadBalancingPolicy = request.LoadBalancingPolicy });
            return new RouteOperationResult(true, $"Cluster '{request.ClusterId}' created successfully");
        }
        finally
        {
            _rwLock.ExitWriteLock();
            if (saveNeeded)
                await SaveDynamicConfigAsync();
        }
    }

    /// <summary>
    /// Update an existing cluster.
    /// </summary>
    /// <param name="clusterId">Cluster ID to update.</param>
    /// <param name="request">Cluster update request.</param>
    /// <returns>Route operation result.</returns>
    public async Task<RouteOperationResult> TryUpdateCluster(string clusterId, UpdateClusterRequest request)
    {
        if (string.IsNullOrWhiteSpace(clusterId))
        {
            _auditLog.RecordFailure("UpdateCluster", clusterId ?? "", "Cluster ID cannot be empty");
            return new RouteOperationResult(false, "Cluster ID cannot be empty");
        }

        bool saveNeeded = false;
        _rwLock.EnterWriteLock();
        try
        {
            var config = _configProvider.GetConfig();
            var routes = config.Routes ?? Array.Empty<RouteConfig>();
            var newClusters = new List<ClusterConfig>(config.Clusters ?? []);

            var existingIdx = newClusters.FindIndex(c =>
                string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));

            if (existingIdx < 0)
            {
                _auditLog.RecordFailure("UpdateCluster", clusterId, $"Cluster '{clusterId}' not found");
                return new RouteOperationResult(false, $"Cluster '{clusterId}' not found");
            }

            var existing = newClusters[existingIdx];
            var updated = new ClusterConfig
            {
                ClusterId = existing.ClusterId,
                Destinations = request.Destinations?.ToDictionary(
                    d => d.Key,
                    d => new DestinationConfig { Address = d.Value }) ?? existing.Destinations,
                LoadBalancingPolicy = request.LoadBalancingPolicy ?? existing.LoadBalancingPolicy,
                HealthCheck = request.HealthCheck != null
                    ? BuildClusterHealthCheck(new Models.HealthCheckConfig
                    {
                        Active = request.HealthCheck.Active?.Enabled ?? false,
                        Endpoint = request.HealthCheck.Active?.Path,
                        Passive = request.HealthCheck.Passive?.Enabled ?? false,
                        PassivePolicy = request.HealthCheck.Passive?.Policy,
                        PassiveReactivationPeriod = TimeSpan.TryParse(request.HealthCheck.Passive?.ReactivationPeriod, out var rp) ? rp : TimeSpan.FromSeconds(30),
                        AvailableDestinationsPolicy = request.HealthCheck.AvailableDestinationsPolicy
                    })
                    : existing.HealthCheck
            };

            newClusters[existingIdx] = updated;
            _configProvider.Update(routes, SanitizeClusters(newClusters));

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
                        Endpoint = request.HealthCheck.Active?.Path,
                        Passive = request.HealthCheck.Passive?.Enabled ?? false,
                        PassivePolicy = request.HealthCheck.Passive?.Policy,
                        PassiveReactivationPeriod = TimeSpan.TryParse(request.HealthCheck.Passive?.ReactivationPeriod, out var rp3) ? rp3 : TimeSpan.FromSeconds(30),
                        AvailableDestinationsPolicy = request.HealthCheck.AvailableDestinationsPolicy
                    };
                }
            }

            saveNeeded = true;
            _logger.LogInformation("Cluster '{ClusterId}' updated", clusterId);
            _auditLog.RecordSuccess("UpdateCluster", clusterId, null, null, null,
                new { destinations = request.Destinations, loadBalancingPolicy = request.LoadBalancingPolicy });
            return new RouteOperationResult(true, $"Cluster '{clusterId}' updated successfully");
        }
        finally
        {
            _rwLock.ExitWriteLock();
            if (saveNeeded)
                await SaveDynamicConfigAsync();
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
    /// Build YARP HealthCheckConfig from our internal HealthCheckConfig model.
    /// </summary>
    private static global::Yarp.ReverseProxy.Configuration.HealthCheckConfig? BuildClusterHealthCheck(Models.HealthCheckConfig? config)
    {
        if (config == null) return null;

        var result = new global::Yarp.ReverseProxy.Configuration.HealthCheckConfig
        {
            Active = config.Active
                ? new global::Yarp.ReverseProxy.Configuration.ActiveHealthCheckConfig
                {
                    Enabled = true,
                    Interval = config.Interval,
                    Timeout = config.Timeout,
                    Path = config.Endpoint ?? "/health"
                }
                : null,
            Passive = config.Passive
                ? new global::Yarp.ReverseProxy.Configuration.PassiveHealthCheckConfig
                {
                    Enabled = true,
                    Policy = config.PassivePolicy ?? "ConsecutiveFailures",
                    ReactivationPeriod = config.PassiveReactivationPeriod
                }
                : null,
            AvailableDestinationsPolicy = config.AvailableDestinationsPolicy ?? null
        };

        return result;
    }

    /// <summary>
    /// Reapply dynamic configuration to YARP.
    /// Called after programmatic modifications to the dynamic config object.
    /// </summary>
    public void RefreshConfig()
    {
        _rwLock.EnterWriteLock();
        try
        {
            ApplyDynamicConfigToYarp();
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Save dynamic configuration to persistence file.
    /// Called after programmatic modifications to the dynamic config object.
    /// </summary>
    public async Task SaveDynamicConfig()
    {
        _rwLock.EnterWriteLock();
        try
        {
            await SaveDynamicConfigAsync();
        }
        finally
        {
            _rwLock.ExitWriteLock();
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
    public async Task ReplaceAllConfig(
        IReadOnlyList<RouteConfig> newRoutes,
        IReadOnlyList<ClusterConfig> newClusters,
        string source = "rollback",
        string? createdBy = "dashboard-user")
    {
        _rwLock.EnterWriteLock();
        try
        {
            // Update YARP in-memory config in one operation
            _configProvider.Update(newRoutes, SanitizeClusters(newClusters));

            // Update dynamic config metadata
            EnsureDynamicConfigInitialized();

            // Build lookups of existing metadata to preserve during rollback
            var existingClusters = _dynamicConfig!.Clusters.ToDictionary(
                c => c.ClusterId,
                c => c,
                StringComparer.OrdinalIgnoreCase);
            var existingRoutes = _dynamicConfig.Routes.ToDictionary(
                r => r.RouteId,
                r => r,
                StringComparer.OrdinalIgnoreCase);

            _dynamicConfig.Routes.Clear();
            _dynamicConfig.Clusters.Clear();

            foreach (var cluster in newClusters)
            {
                // Preserve original source if the cluster existed before
                if (existingClusters.TryGetValue(cluster.ClusterId ?? string.Empty, out var existing))
                {
                    var dynCluster = existing;
                    dynCluster.Destinations = cluster.Destinations?.ToDictionary(
                        d => d.Key,
                        d => d.Value.Address ?? string.Empty) ?? new Dictionary<string, string>();
                    dynCluster.LoadBalancingPolicy = cluster.LoadBalancingPolicy;
                    _dynamicConfig.Clusters.Add(dynCluster);
                }
                else
                {
                    var dynCluster = new DynamicClusterConfig
                    {
                        ClusterId = cluster.ClusterId ?? string.Empty,
                        Destinations = cluster.Destinations?.ToDictionary(
                            d => d.Key,
                            d => d.Value.Address ?? string.Empty) ?? new Dictionary<string, string>(),
                        LoadBalancingPolicy = cluster.LoadBalancingPolicy ?? string.Empty,
                        Source = source,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = createdBy
                    };
                    _dynamicConfig.Clusters.Add(dynCluster);
                }
            }

            foreach (var route in newRoutes)
            {
                // Preserve original source if the route existed before
                if (existingRoutes.TryGetValue(route.RouteId ?? string.Empty, out var existing))
                {
                    existing.ClusterId = route.ClusterId ?? string.Empty;
                    existing.MatchPath = route.Match?.Path ?? string.Empty;
                    existing.Order = route.Order ?? 50;
                    existing.Transforms = route.Transforms?.Select(t => new Dictionary<string, string>(t)).ToList();
                    _dynamicConfig.Routes.Add(existing);
                }
                else
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
            }

            // Save to file only once at the end
            _logger.LogInformation(
                "Configuration replaced: {Routes} routes, {Clusters} clusters",
                newRoutes.Count,
                newClusters.Count);
            _auditLog.RecordSuccess("RollbackConfig", "full config", createdBy, null,
                null,
                new { routeCount = newRoutes.Count, clusterCount = newClusters.Count });
        }
        finally
        {
            _rwLock.ExitWriteLock();
            await SaveDynamicConfigAsync();
        }
    }

    /// <summary>
    /// Update heartbeat timestamp for a registered service.
    /// Used by auto-registered services to keep their registration alive.
    /// </summary>
    /// <param name="routeName">Route name to update.</param>
    /// <param name="clientIp">Optional client IP for IP-based isolation.</param>
    /// <returns>True if route was found and heartbeat updated.</returns>
    public bool UpdateHeartbeat(string routeName, string? clientIp = null)
    {
        _rwLock.EnterWriteLock();
        try
        {
            EnsureDynamicConfigInitialized();

            // Find the route and its cluster
            var route = _dynamicConfig!.Routes.FirstOrDefault(r =>
                r.RouteId.Equals(routeName, StringComparison.OrdinalIgnoreCase));

            if (route == null)
                return false;

            // Update heartbeat on the associated cluster
            var cluster = _dynamicConfig.Clusters.FirstOrDefault(c =>
                c.ClusterId.Equals(route.ClusterId, StringComparison.OrdinalIgnoreCase));

            if (cluster != null)
            {
                cluster.LastHeartbeat = DateTime.UtcNow;
                return true;
            }

            return false;
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }
}
