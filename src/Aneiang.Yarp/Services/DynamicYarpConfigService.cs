using System.Threading;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Dynamic YARP config service: add, update, and delete routes and clusters at runtime.
/// Thread-safe with SemaphoreSlim protection (fixes ReaderWriterLockSlim + await thread-affinity bug).
/// Persistence via <see cref="IRouteRepository"/> and <see cref="IClusterRepository"/> (SQLite by default).
/// </summary>
public class DynamicYarpConfigService : IDynamicYarpConfigService, IHostedService
{
    private readonly InMemoryConfigProvider _configProvider;
    private readonly IRouteRepository _routeRepo;
    private readonly IClusterRepository _clusterRepo;
    private readonly IConfigChangeAuditLog _auditLog;
    private readonly ILogger<DynamicYarpConfigService> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private GatewayDynamicConfig? _dynamicConfig;

    /// <summary>Monotonically increasing version for detecting in-memory vs persisted drift.</summary>
    private long _configVersion;

    /// <summary>IDs of routes from appsettings.json (static config), populated on startup.</summary>
    private readonly HashSet<string> _staticRouteIds = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>IDs of clusters from appsettings.json (static config), populated on startup.</summary>
    private readonly HashSet<string> _staticClusterIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of DynamicYarpConfigService.
    /// </summary>
    public DynamicYarpConfigService(
        InMemoryConfigProvider configProvider,
        IRouteRepository routeRepo,
        IClusterRepository clusterRepo,
        IConfigChangeAuditLog auditLog,
        ILogger<DynamicYarpConfigService> logger)
    {
        _configProvider = configProvider;
        _routeRepo = routeRepo;
        _clusterRepo = clusterRepo;
        _auditLog = auditLog;
        _logger = logger;
    }

    /// <summary>
    /// IHostedService.StartAsync — loads dynamic config from repository after schema migration.
    /// This is awaited during host startup so that all runtime operations see a fully-loaded config.
    /// </summary>
    Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        LoadDynamicConfig();
        return Task.CompletedTask;
    }

    /// <summary>
    /// IHostedService.StopAsync — no-op.
    /// </summary>
    Task IHostedService.StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Load dynamic configuration from repository on startup.
    /// Validates consistency between persisted data and YARP in-memory state.
    /// </summary>
    private void LoadDynamicConfig()
    {
        _logger.LogInformation("[DynamicYarpConfigService] LoadDynamicConfig starting...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            _dynamicConfig = LoadConfigFromRepository();

            // Apply dynamic config to YARP
            if (_dynamicConfig != null && (_dynamicConfig.Routes.Count > 0 || _dynamicConfig.Clusters.Count > 0))
            {
                _logger.LogInformation("[DynamicYarpConfigService] Applying {RouteCount} routes and {ClusterCount} clusters to YARP...",
                    _dynamicConfig.Routes.Count, _dynamicConfig.Clusters.Count);
                ApplyDynamicConfigToYarp();
                _logger.LogInformation(
                    "Loaded {RouteCount} dynamic routes and {ClusterCount} dynamic clusters from repository",
                    _dynamicConfig.Routes.Count,
                    _dynamicConfig.Clusters.Count);
            }
            else
            {
                _logger.LogInformation("[DynamicYarpConfigService] No dynamic routes/clusters to apply");
            }

            // Mark static config from appsettings.json as "config" source
            _logger.LogInformation("[DynamicYarpConfigService] Marking static config...");
            MarkStaticConfig();

            // Startup consistency check
            _logger.LogInformation("[DynamicYarpConfigService] Validating consistency...");
            ValidateConsistency();

            _logger.LogInformation("[DynamicYarpConfigService] LoadDynamicConfig completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DynamicYarpConfigService] Failed to load dynamic config on startup after {ElapsedMs}ms", sw.ElapsedMilliseconds);
            _dynamicConfig = new GatewayDynamicConfig();
        }
    }

    /// <summary>
    /// Load config from repository, mapping entities to domain models.
    /// </summary>
    private GatewayDynamicConfig LoadConfigFromRepository()
    {
        _logger.LogInformation("[DynamicYarpConfigService] LoadConfigFromRepository starting...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            _logger.LogInformation("[DynamicYarpConfigService] Loading routes...");
            var routeEntities = _routeRepo.GetAllRoutesAsync().GetAwaiter().GetResult();
            _logger.LogInformation("[DynamicYarpConfigService] Loaded {Count} routes in {ElapsedMs}ms", routeEntities.Count, sw.ElapsedMilliseconds);

            sw.Restart();
            _logger.LogInformation("[DynamicYarpConfigService] Loading clusters...");
            var clusterEntities = _clusterRepo.GetAllClustersAsync().GetAwaiter().GetResult();
            _logger.LogInformation("[DynamicYarpConfigService] Loaded {Count} clusters in {ElapsedMs}ms", clusterEntities.Count, sw.ElapsedMilliseconds);

            var config = new GatewayDynamicConfig
            {
                Routes = routeEntities.ToRouteConfigs()
            };

            foreach (var clusterEntity in clusterEntities)
            {
                var cluster = clusterEntity.ToClusterConfig();
                var destEntities = _clusterRepo.GetDestinationsAsync(clusterEntity.ClusterId).GetAwaiter().GetResult();
                cluster.Destinations = destEntities.ToDestinations();
                config.Clusters.Add(cluster);
            }

            _logger.LogInformation("[DynamicYarpConfigService] LoadConfigFromRepository completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DynamicYarpConfigService] Failed to load config from repository after {ElapsedMs}ms, starting with empty config", sw.ElapsedMilliseconds);
            return new GatewayDynamicConfig();
        }
    }

    /// <summary>
    /// Validates that the in-memory YARP config matches the persisted repository.
    /// </summary>
    private void ValidateConsistency()
    {
        try
        {
            var yarpConfig = _configProvider.GetConfig();

            if (_dynamicConfig != null &&
                yarpConfig.Routes.Count != _dynamicConfig.Routes.Count)
            {
                _logger.LogWarning(
                    "Route count mismatch: YARP={YarpCount}, Repository={RepoCount}. Re-syncing YARP.",
                    yarpConfig.Routes.Count, _dynamicConfig.Routes.Count);
                ApplyDynamicConfigToYarp();
            }

            if (_dynamicConfig != null &&
                yarpConfig.Clusters.Count != _dynamicConfig.Clusters.Count)
            {
                _logger.LogWarning(
                    "Cluster count mismatch: YARP={YarpCount}, Repository={RepoCount}. Re-syncing YARP.",
                    yarpConfig.Clusters.Count, _dynamicConfig.Clusters.Count);
                ApplyDynamicConfigToYarp();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Consistency validation failed, continuing with current state");
        }
    }

    /// <summary>
    /// Record routes and clusters from appsettings.json (static config) IDs.
    /// Ensures all static config items exist in _dynamicConfig for persistence.
    /// </summary>
    private void MarkStaticConfig()
    {
        try
        {
            EnsureDynamicConfigInitialized();
            var currentConfig = _configProvider.GetConfig();

            var thisStartupStaticRoutes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var thisStartupStaticClusters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (currentConfig.Routes != null)
            {
                foreach (var route in currentConfig.Routes)
                {
                    var routeId = route.RouteId ?? string.Empty;
                    thisStartupStaticRoutes.Add(routeId);
                }

                _dynamicConfig!.Routes.RemoveAll(r =>
                    !thisStartupStaticRoutes.Contains(r.RouteId));

                foreach (var route in currentConfig.Routes)
                {
                    var routeId = route.RouteId ?? string.Empty;
                    _staticRouteIds.Add(routeId);

                    var existingRoute = _dynamicConfig!.Routes.FirstOrDefault(r =>
                        string.Equals(r.RouteId, routeId, StringComparison.OrdinalIgnoreCase));

                    if (existingRoute == null)
                    {
                        _dynamicConfig.Routes.Add(new DynamicRouteConfig
                        {
                            RouteId = routeId,
                            ClusterUid = ResolveClusterUid(route.ClusterId ?? string.Empty),
                            ClusterId = route.ClusterId ?? string.Empty,
                            MatchPath = route.Match?.Path!,
                            Order = route.Order ?? 50,
                            Transforms = route.Transforms?.Select(t => new Dictionary<string, string>(t)).ToList(),
                            Source = "config",
                            CreatedAt = DateTime.UtcNow,
                            CreatedBy = "appsettings.json"
                        });
                    }
                    else
                    {
                        existingRoute.ClusterUid = ResolveClusterUid(route.ClusterId ?? string.Empty);
                        existingRoute.ClusterId = route.ClusterId ?? string.Empty;
                        existingRoute.MatchPath = route.Match?.Path ?? string.Empty;
                        existingRoute.Order = route.Order ?? 50;
                        existingRoute.Transforms = route.Transforms?.Select(t => new Dictionary<string, string>(t)).ToList();
                    }
                }
            }

            if (currentConfig.Clusters != null)
            {
                foreach (var cluster in currentConfig.Clusters)
                {
                    var clusterId = cluster.ClusterId ?? string.Empty;
                    thisStartupStaticClusters.Add(clusterId);
                }

                _dynamicConfig!.Clusters.RemoveAll(c =>
                    !thisStartupStaticClusters.Contains(c.ClusterId));

                foreach (var cluster in currentConfig.Clusters)
                {
                    var clusterId = cluster.ClusterId ?? string.Empty;
                    _staticClusterIds.Add(clusterId);

                    var existingCluster = _dynamicConfig!.Clusters.FirstOrDefault(c =>
                        string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));

                    if (existingCluster == null)
                    {
                        var destinations = cluster.Destinations?.ToDictionary(
                            d => d.Key,
                            d => d.Value.Address ?? string.Empty) ?? new Dictionary<string, string>();

                        _dynamicConfig.Clusters.Add(new DynamicClusterConfig
                        {
                            ClusterId = clusterId,
                            Destinations = destinations,
                            LoadBalancingPolicy = cluster.LoadBalancingPolicy ?? string.Empty,
                            Source = "config",
                            CreatedAt = DateTime.UtcNow,
                            CreatedBy = "appsettings.json"
                        });
                    }
                    else
                    {
                        existingCluster.Destinations = cluster.Destinations?.ToDictionary(
                            d => d.Key,
                            d => d.Value.Address ?? string.Empty) ?? new Dictionary<string, string>();
                        existingCluster.LoadBalancingPolicy = cluster.LoadBalancingPolicy ?? string.Empty;
                    }
                }
            }

            // Persist cleaned config
            PersistConfigToRepositorySync();

            _logger.LogDebug(
                "Synced {TotalRoutes} routes and {TotalClusters} clusters. Static route IDs: [{StaticRoutes}], Static cluster IDs: [{StaticClusters}]",
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
                Transforms = dynRoute.Transforms?.Select(t => (IReadOnlyDictionary<string, string>)t).ToList(),
                Metadata = dynRoute.Metadata.Count > 0 ? dynRoute.Metadata : null
            };

            if (existingIdx >= 0)
                routes[existingIdx] = routeConfig;
            else
                routes.Add(routeConfig);
        }

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
    /// Persist the entire dynamic config to repository synchronously (startup only).
    /// </summary>
    internal void PersistConfigToRepositorySync()
    {
        if (_dynamicConfig == null)
            _dynamicConfig = new GatewayDynamicConfig();

        PersistConfigToRepositoryAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Persist the entire dynamic config to repository asynchronously (runtime operations).
    /// </summary>
    private async Task PersistConfigToRepositoryAsync(string operationName = "config-update", string? targetName = null)
    {
        if (_dynamicConfig == null)
            _dynamicConfig = new GatewayDynamicConfig();

        try
        {
            var config = _dynamicConfig;

            if (await TryPersistIncrementalAsync(config, operationName, targetName))
                return;

            var targetRouteIds = new HashSet<string>(config.Routes.Select(r => r.RouteId), StringComparer.OrdinalIgnoreCase);
            var targetClusterIds = new HashSet<string>(config.Clusters.Select(c => c.ClusterId), StringComparer.OrdinalIgnoreCase);

            // Clean up stale routes
            var existingRoutes = await _routeRepo.GetAllRoutesAsync();
            foreach (var existing in existingRoutes)
            {
                if (!targetRouteIds.Contains(existing.RouteId))
                {
                    await _routeRepo.DeleteRouteAsync(existing.RouteId);
                    _logger.LogDebug("Deleted stale route '{RouteId}'", existing.RouteId);
                }
            }

            // Save current routes
            var routeEntities = config.Routes.Select(r => r.ToEntity()).ToList();
            await _routeRepo.SaveRoutesAsync(routeEntities);

            // Clean up stale clusters + destinations
            var existingClusters = await _clusterRepo.GetAllClustersAsync();
            foreach (var existing in existingClusters)
            {
                if (!targetClusterIds.Contains(existing.ClusterId))
                {
                    await _clusterRepo.DeleteDestinationsAsync(existing.ClusterId);
                    await _clusterRepo.DeleteClusterAsync(existing.ClusterId);
                    _logger.LogDebug("Deleted stale cluster '{ClusterId}'", existing.ClusterId);
                }
            }

            // Save current clusters
            var clusterEntities = config.Clusters.Select(c => c.ToEntity()).ToList();
            await _clusterRepo.SaveClustersAsync(clusterEntities);

            // Save destinations for each cluster
            foreach (var cluster in config.Clusters)
            {
                var destEntities = cluster.Destinations.Select(d => d.ToEntity(cluster.ClusterId)).ToList();
                _logger.LogDebug(
                    "Full persist: saving cluster '{ClusterId}' with {DestinationCount} destinations",
                    cluster.ClusterId, destEntities.Count);
                await _clusterRepo.SaveDestinationsAsync(cluster.ClusterId, destEntities);
            }

            _logger.LogInformation(
                "Full dynamic config persisted: {RouteCount} routes, {ClusterCount} clusters",
                config.Routes.Count, config.Clusters.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist dynamic config for {OperationName}. Target: {TargetName}, RouteCount: {RouteCount}, ClusterCount: {ClusterCount}",
                operationName,
                targetName ?? "n/a",
                _dynamicConfig.Routes.Count,
                _dynamicConfig.Clusters.Count);
            throw;
        }
    }

    private async Task<bool> TryPersistIncrementalAsync(GatewayDynamicConfig config, string operationName, string? targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName)) return false;

        if (operationName is "AddOrUpdateRoute" or "UpdateRouteMetadata")
        {
            var route = config.Routes.FirstOrDefault(r => string.Equals(r.RouteId, targetName, StringComparison.OrdinalIgnoreCase));
            if (route == null) return false;

            await _routeRepo.SaveRouteAsync(route.ToEntity());
            return true;
        }

        if (operationName is "AddCluster" or "UpdateCluster" or "CreateCluster" or "UpdateClusterCircuitBreaker")
        {
            var cluster = config.Clusters.FirstOrDefault(c => string.Equals(c.ClusterId, targetName, StringComparison.OrdinalIgnoreCase));
            if (cluster == null)
            {
                _logger.LogWarning("Incremental cluster persist skipped: cluster '{ClusterId}' not found in dynamic config", targetName);
                return false;
            }

            var destEntities = cluster.Destinations.Select(d => d.ToEntity(cluster.ClusterId)).ToList();
            _logger.LogDebug(
                "Incrementally persisting cluster '{ClusterId}' with {DestinationCount} destinations",
                cluster.ClusterId, destEntities.Count);

            await _clusterRepo.SaveClusterAsync(cluster.ToEntity());
            await _clusterRepo.SaveDestinationsAsync(cluster.ClusterId, destEntities);

            // Verify destinations were actually persisted
            var savedDestinations = await _clusterRepo.GetDestinationsAsync(cluster.ClusterId, CancellationToken.None);
            _logger.LogInformation(
                "Incrementally persisted cluster '{ClusterId}': requested {RequestedCount}, verified {VerifiedCount} destinations in DB",
                cluster.ClusterId, destEntities.Count, savedDestinations.Count);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Sanitize cluster list by removing destinations with empty/null addresses.
    /// </summary>
    private IReadOnlyList<ClusterConfig> SanitizeClusters(IReadOnlyList<ClusterConfig> clusters)
    {
        var sanitized = new List<ClusterConfig>();
        foreach (var cluster in clusters)
        {
            if (cluster.Destinations == null || cluster.Destinations.Count == 0)
            {
                sanitized.Add(cluster);
                continue;
            }

            var validDests = new Dictionary<string, DestinationConfig>(
                cluster.Destinations.Where(d =>
                    !string.IsNullOrWhiteSpace(d.Value?.Address)));

            if (validDests.Count < cluster.Destinations.Count)
            {
                _logger.LogWarning(
                    "Dropped {InvalidCount} invalid destinations from cluster {ClusterId}",
                    cluster.Destinations.Count - validDests.Count,
                    cluster.ClusterId ?? "unknown");
            }

            sanitized.Add(new ClusterConfig
            {
                ClusterId = cluster.ClusterId ?? string.Empty,
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

    // ── TryAddRoute ──────────────────────────────────────────────────────

    public async Task<RouteOperationResult> TryAddRoute(
        RegisterRouteRequest request,
        string source = "dynamic",
        string? createdBy = null)
    {
        bool saveNeeded = false;
        await _semaphore.WaitAsync();
        try
        {
            var config = _configProvider.GetConfig();
            var newRoutes = new List<RouteConfig>(config.Routes ?? []);
            var newClusters = new List<ClusterConfig>(config.Clusters ?? []);

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

            Dictionary<string, string>? existingMetadata = null;
            if (existingRouteIdx >= 0)
            {
                existingMetadata = newRoutes[existingRouteIdx].Metadata as Dictionary<string, string>;
            }

            bool isNew;
            if (existingRouteIdx >= 0)
            {
                newRoutes[existingRouteIdx] = new RouteConfig
                {
                    RouteId = request.RouteName,
                    ClusterId = request.ClusterName,
                    Match = new RouteMatch { Path = request.MatchPath },
                    Order = request.Order ?? 50,
                    Transforms = request.Transforms?.Select(t => (IReadOnlyDictionary<string, string>)t).ToList(),
                    Metadata = existingMetadata
                };
                isNew = false;
                _logger.LogDebug("Route '{RouteName}' exists, updating", request.RouteName);
            }
            else
            {
                newRoutes.Add(routeConfig);
                isNew = true;
                _logger.LogDebug("Route '{RouteName}' is new, adding", request.RouteName);
            }

            // Cluster: create or update
            if (request.UseIpIsolation && !string.IsNullOrWhiteSpace(request.ClientIp))
            {
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
            }
            else
            {
                var existingClusterIdx = newClusters.FindIndex(c =>
                    string.Equals(c.ClusterId, request.ClusterName, StringComparison.OrdinalIgnoreCase));

                if (existingClusterIdx >= 0)
                {
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
                        }
                    });
                }
            }

            _configProvider.Update(newRoutes, SanitizeClusters(newClusters));

            EnsureDynamicConfigInitialized();

            var dynRoute = _dynamicConfig!.Routes.FirstOrDefault(r =>
                string.Equals(r.RouteId, request.RouteName, StringComparison.OrdinalIgnoreCase));

            if (dynRoute == null)
            {
                dynRoute = new DynamicRouteConfig
                {
                    RouteId = request.RouteName,
                    ClusterUid = ResolveClusterUid(request.ClusterName),
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
                dynRoute.ClusterUid = ResolveClusterUid(request.ClusterName);
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
            if (saveNeeded)
            {
                Interlocked.Increment(ref _configVersion);
                _dynamicConfig!.Version = _configVersion;
                await PersistConfigToRepositoryAsync("AddOrUpdateRoute", request.RouteName);
            }
            _semaphore.Release();
        }
    }

    // ── TryRemoveRoute ───────────────────────────────────────────────────

    public async Task<RouteOperationResult> TryRemoveRoute(string routeName, string? clientIp = null, bool removeOrphanedCluster = true)
    {
        if (string.IsNullOrWhiteSpace(routeName))
            return new RouteOperationResult(false, "Route name cannot be empty");

        bool saveNeeded = false;
        await _semaphore.WaitAsync();
        try
        {
            var config = _configProvider.GetConfig();
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

            // IP isolation: only remove the matching destination
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

                    var mutableClusters = new List<ClusterConfig>(clusters);
                    var clusterIdx = mutableClusters.FindIndex(c =>
                        string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));

                    if (clusterIdx >= 0)
                    {
                        if (destinations.Count == 0)
                        {
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
                        _logger.LogDebug(
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
            if (saveNeeded)
            {
                Interlocked.Increment(ref _configVersion);
                _dynamicConfig!.Version = _configVersion;
                await PersistConfigToRepositoryAsync("RemoveRoute", routeName);
            }
            _semaphore.Release();
        }
    }

    // ── TryAddCluster (basic overload) ───────────────────────────────────

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
            _auditLog.RecordFailure("AddCluster", clusterId, "At least one destination is required");
            return new RouteOperationResult(false, "At least one destination is required");
        }

        bool saveNeeded = false;
        bool isNew = false;
        await _semaphore.WaitAsync();
        try
        {
            var config = _configProvider.GetConfig();
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

            if (existingClusterIdx >= 0)
            {
                newClusters[existingClusterIdx] = clusterConfig;
                isNew = false;
            }
            else
            {
                newClusters.Add(clusterConfig);
                isNew = true;
            }

            _configProvider.Update(routes, SanitizeClusters(newClusters));

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
            if (saveNeeded)
            {
                Interlocked.Increment(ref _configVersion);
                _dynamicConfig!.Version = _configVersion;
                await PersistConfigToRepositoryAsync(isNew ? "AddCluster" : "UpdateCluster", clusterId);
            }
            _semaphore.Release();
        }
    }

    // ── TryAddCluster (CreateClusterRequest) ─────────────────────────────

    public async Task<RouteOperationResult> TryAddCluster(
        CreateClusterRequest request,
        string source = "dynamic",
        string? createdBy = null)
    {
        bool saveNeeded = false;
        await _semaphore.WaitAsync();
        try
        {
            var config = _configProvider.GetConfig();
            var clusters = config.Clusters ?? Array.Empty<ClusterConfig>();

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

            EnsureDynamicConfigInitialized();
            _dynamicConfig!.Clusters.Add(new DynamicClusterConfig
            {
                ClusterId = request.ClusterId,
                Destinations = request.Destinations,
                LoadBalancingPolicy = request.LoadBalancingPolicy,
                HealthCheck = request.HealthCheck != null ? new Models.HealthCheckConfig
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
            _logger.LogDebug("Cluster '{ClusterId}' created with {DestCount} destinations",
                request.ClusterId, request.Destinations.Count);
            _auditLog.RecordSuccess("AddCluster", request.ClusterId, createdBy, null, null,
                new { destinations = request.Destinations, loadBalancingPolicy = request.LoadBalancingPolicy });
            return new RouteOperationResult(true, $"Cluster '{request.ClusterId}' created successfully");
        }
        finally
        {
            if (saveNeeded)
            {
                Interlocked.Increment(ref _configVersion);
                _dynamicConfig!.Version = _configVersion;
                await PersistConfigToRepositoryAsync("CreateCluster", request.ClusterId);
            }
            _semaphore.Release();
        }
    }

    // ── TryRenameRoute ──────────────────────────────────────────────────

    public async Task<RouteOperationResult> TryRenameRoute(
        string oldRouteId,
        string newRouteId,
        RegisterRouteRequest request,
        string source = "dashboard",
        string? createdBy = "dashboard-user")
    {
        if (string.IsNullOrWhiteSpace(oldRouteId) || string.IsNullOrWhiteSpace(newRouteId))
        {
            _auditLog.RecordFailure("RenameRoute", oldRouteId ?? "", "Route ID cannot be empty");
            return new RouteOperationResult(false, "Route ID cannot be empty");
        }

        if (string.Equals(oldRouteId, newRouteId, StringComparison.OrdinalIgnoreCase))
        {
            return new RouteOperationResult(false, "Old and new route IDs are the same");
        }

        bool saveNeeded = false;
        await _semaphore.WaitAsync();
        try
        {
            var config = _configProvider.GetConfig();
            var newRoutes = new List<RouteConfig>(config.Routes ?? []);
            var clusters = config.Clusters ?? [];

            var oldRoute = newRoutes.FirstOrDefault(r =>
                string.Equals(r.RouteId, oldRouteId, StringComparison.OrdinalIgnoreCase));
            if (oldRoute == null)
            {
                _auditLog.RecordFailure("UpdateRoute", oldRouteId, $"Route '{oldRouteId}' not found");
                return new RouteOperationResult(false, $"Route '{oldRouteId}' not found");
            }

            if (newRoutes.Any(r =>
                string.Equals(r.RouteId, newRouteId, StringComparison.OrdinalIgnoreCase)))
            {
                _auditLog.RecordFailure("UpdateRoute", oldRouteId,
                    $"Target route '{newRouteId}' already exists");
                return new RouteOperationResult(false, $"Target route '{newRouteId}' already exists");
            }

            // Create the new route config preserving all settings from the old route
            var newRoute = new RouteConfig
            {
                RouteId = newRouteId,
                ClusterId = request.ClusterName ?? oldRoute.ClusterId,
                Match = new RouteMatch { Path = request.MatchPath ?? (oldRoute.Match?.Path ?? string.Empty) },
                Order = request.Order ?? oldRoute.Order,
                Transforms = request.Transforms?.Select(t => (IReadOnlyDictionary<string, string>)t).ToList()
                    ?? oldRoute.Transforms,
                AuthorizationPolicy = oldRoute.AuthorizationPolicy,
                CorsPolicy = oldRoute.CorsPolicy,
                Metadata = oldRoute.Metadata
            };
            newRoutes.Add(newRoute);

            // Remove the old route
            newRoutes.RemoveAll(r =>
                string.Equals(r.RouteId, oldRouteId, StringComparison.OrdinalIgnoreCase));

            _configProvider.Update(newRoutes, SanitizeClusters(clusters));

            EnsureDynamicConfigInitialized();

            // Update _dynamicConfig
            var dynRoute = _dynamicConfig!.Routes.FirstOrDefault(r =>
                string.Equals(r.RouteId, oldRouteId, StringComparison.OrdinalIgnoreCase));
            if (dynRoute != null)
            {
                var renamedDynRoute = new DynamicRouteConfig
                {
                    RouteUid = dynRoute.RouteUid,
                    RouteId = newRouteId,
                    ClusterUid = ResolveClusterUid(request.ClusterName ?? dynRoute.ClusterId),
                    ClusterId = request.ClusterName ?? dynRoute.ClusterId,
                    MatchPath = request.MatchPath ?? dynRoute.MatchPath,
                    Order = request.Order ?? dynRoute.Order,
                    Transforms = request.Transforms ?? dynRoute.Transforms,
                    Source = source,
                    CreatedAt = dynRoute.CreatedAt,
                    CreatedBy = createdBy,
                    Metadata = new Dictionary<string, string>(dynRoute.Metadata)
                };
                _dynamicConfig.Routes.RemoveAll(r =>
                    string.Equals(r.RouteId, oldRouteId, StringComparison.OrdinalIgnoreCase));
                _dynamicConfig.Routes.Add(renamedDynRoute);
            }

            saveNeeded = true;

            _logger.LogInformation(
                "Route '{OldRouteId}' renamed to '{NewRouteId}'",
                oldRouteId, newRouteId);
            _auditLog.RecordSuccess("UpdateRoute", $"{oldRouteId} → {newRouteId}", createdBy, null,
                new { oldRouteId, action = "rename" },
                new { newRouteId, clusterId = request.ClusterName, matchPath = request.MatchPath, action = "rename" });
            return new RouteOperationResult(true,
                $"Route '{oldRouteId}' renamed to '{newRouteId}'");
        }
        finally
        {
            if (saveNeeded)
            {
                Interlocked.Increment(ref _configVersion);
                _dynamicConfig!.Version = _configVersion;
                await PersistConfigToRepositoryAsync("UpdateRoute", oldRouteId);
            }
            _semaphore.Release();
        }
    }

    // ── TryUpdateCluster ─────────────────────────────────────────────────

    public async Task<RouteOperationResult> TryUpdateCluster(string clusterId, UpdateClusterRequest request)
    {
        if (string.IsNullOrWhiteSpace(clusterId))
        {
            _auditLog.RecordFailure("UpdateCluster", clusterId ?? "", "Cluster ID cannot be empty");
            return new RouteOperationResult(false, "Cluster ID cannot be empty");
        }

        bool saveNeeded = false;
        await _semaphore.WaitAsync();
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

            EnsureDynamicConfigInitialized();
            var dynCluster = _dynamicConfig!.Clusters.FirstOrDefault(c =>
                string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));

            if (dynCluster != null)
            {
                if (request.Destinations != null) dynCluster.Destinations = request.Destinations;
                if (request.LoadBalancingPolicy != null) dynCluster.LoadBalancingPolicy = request.LoadBalancingPolicy;
                if (request.HealthCheck != null)
                {
                    dynCluster.HealthCheck = new Models.HealthCheckConfig
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
            _logger.LogDebug("Cluster '{ClusterId}' updated", clusterId);
            _auditLog.RecordSuccess("UpdateCluster", clusterId, null, null, null,
                new { destinations = request.Destinations, loadBalancingPolicy = request.LoadBalancingPolicy });
            return new RouteOperationResult(true, $"Cluster '{clusterId}' updated successfully");
        }
        finally
        {
            if (saveNeeded)
            {
                Interlocked.Increment(ref _configVersion);
                _dynamicConfig!.Version = _configVersion;
                await PersistConfigToRepositoryAsync("UpdateCluster", clusterId);
            }
            _semaphore.Release();
        }
    }

    // ── TryRemoveCluster ─────────────────────────────────────────────────

    public async Task<RouteOperationResult> TryRemoveCluster(string clusterId)
    {
        if (string.IsNullOrWhiteSpace(clusterId))
            return new RouteOperationResult(false, "Cluster ID cannot be empty");

        bool saveNeeded = false;
        await _semaphore.WaitAsync();
        try
        {
            var config = _configProvider.GetConfig();
            var routes = config.Routes ?? Array.Empty<RouteConfig>();
            var clusters = config.Clusters ?? Array.Empty<ClusterConfig>();

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
            if (saveNeeded)
            {
                Interlocked.Increment(ref _configVersion);
                _dynamicConfig!.Version = _configVersion;
                await PersistConfigToRepositoryAsync("RemoveCluster", clusterId);
            }
            _semaphore.Release();
        }
    }

    // ── TryRenameCluster ─────────────────────────────────────────────────

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
            _auditLog.RecordFailure("UpdateCluster", oldClusterId ?? "", "Cluster ID cannot be empty");
            return new RouteOperationResult(false, "Cluster ID cannot be empty");
        }

        if (string.Equals(oldClusterId, newClusterId, StringComparison.OrdinalIgnoreCase))
        {
            _auditLog.RecordFailure("UpdateCluster", oldClusterId, "Old and new cluster IDs are the same");
            return new RouteOperationResult(false, "Old and new cluster IDs are the same");
        }

        bool saveNeeded = false;
        await _semaphore.WaitAsync();
        try
        {
            var config = _configProvider.GetConfig();
            var newRoutes = new List<RouteConfig>(config.Routes ?? []);
            var newClusters = new List<ClusterConfig>(config.Clusters ?? []);

            var oldCluster = newClusters.FirstOrDefault(c =>
                string.Equals(c.ClusterId, oldClusterId, StringComparison.OrdinalIgnoreCase));
            if (oldCluster == null)
            {
                _auditLog.RecordFailure("UpdateCluster", oldClusterId, $"Cluster '{oldClusterId}' not found");
                return new RouteOperationResult(false, $"Cluster '{oldClusterId}' not found");
            }

            if (newClusters.Any(c =>
                string.Equals(c.ClusterId, newClusterId, StringComparison.OrdinalIgnoreCase)))
            {
                _auditLog.RecordFailure("UpdateCluster", oldClusterId, $"Cluster '{newClusterId}' already exists");
                return new RouteOperationResult(false, $"Cluster '{newClusterId}' already exists");
            }

            var newCluster = new ClusterConfig
            {
                ClusterId = newClusterId,
                Destinations = destinations.ToDictionary(d => d.Key, d => new DestinationConfig { Address = d.Value }),
                LoadBalancingPolicy = loadBalancingPolicy,
                HealthCheck = BuildClusterHealthCheck(healthCheck)
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

            newClusters.RemoveAll(c =>
                string.Equals(c.ClusterId, oldClusterId, StringComparison.OrdinalIgnoreCase));

            _configProvider.Update(newRoutes, SanitizeClusters(newClusters));

            EnsureDynamicConfigInitialized();
            var oldDynCluster = _dynamicConfig!.Clusters.FirstOrDefault(c =>
                string.Equals(c.ClusterId, oldClusterId, StringComparison.OrdinalIgnoreCase));
            _dynamicConfig.Clusters.Add(new DynamicClusterConfig
            {
                ClusterUid = oldDynCluster?.ClusterUid ?? Guid.NewGuid().ToString("N"),
                ClusterId = newClusterId,
                Destinations = destinations,
                LoadBalancingPolicy = loadBalancingPolicy,
                HealthCheck = healthCheck,
                Source = source,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            });

            foreach (var dynRoute in _dynamicConfig.Routes.Where(r =>
                string.Equals(r.ClusterId, oldClusterId, StringComparison.OrdinalIgnoreCase)))
            {
                dynRoute.ClusterUid = oldDynCluster?.ClusterUid ?? ResolveClusterUid(newClusterId);
                dynRoute.ClusterId = newClusterId;
            }

            _dynamicConfig.Clusters.RemoveAll(c =>
                string.Equals(c.ClusterId, oldClusterId, StringComparison.OrdinalIgnoreCase));

            saveNeeded = true;

            _logger.LogInformation(
                "Cluster '{OldClusterId}' renamed to '{NewClusterId}', updated {RouteCount} referencing routes",
                oldClusterId, newClusterId, updatedRouteCount);
            _auditLog.RecordSuccess("UpdateCluster", $"{oldClusterId} → {newClusterId}", createdBy, null,
                new { oldClusterId, routesUpdated = updatedRouteCount, action = "rename" },
                new { newClusterId, destinations, loadBalancingPolicy, action = "rename" });
            return new RouteOperationResult(true,
                $"Cluster '{oldClusterId}' renamed to '{newClusterId}', {updatedRouteCount} route(s) updated");
        }
        finally
        {
            if (saveNeeded)
            {
                Interlocked.Increment(ref _configVersion);
                _dynamicConfig!.Version = _configVersion;
                await PersistConfigToRepositoryAsync("UpdateCluster", oldClusterId);
            }
            _semaphore.Release();
        }
    }

    // ── Query methods (read-locked replaced with semaphore) ──────────────

    public IReadOnlyList<RouteConfig> GetRoutes()
    {
        _semaphore.Wait();
        try
        {
            return _configProvider.GetConfig().Routes ?? Array.Empty<RouteConfig>();
        }
        finally { _semaphore.Release(); }
    }

    public IReadOnlyList<ClusterConfig> GetClusters()
    {
        _semaphore.Wait();
        try
        {
            return _configProvider.GetConfig().Clusters ?? Array.Empty<ClusterConfig>();
        }
        finally { _semaphore.Release(); }
    }

    public ClusterConfig? GetCluster(string clusterId)
    {
        var clusters = GetClusters();
        return clusters.FirstOrDefault(c =>
            string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
    }

    public GatewayDynamicConfig? GetDynamicConfig() => _dynamicConfig;

    // ── RefreshConfig / SaveDynamicConfig / ReplaceAllConfig ──────────────

    public void RefreshConfig()
    {
        _semaphore.Wait();
        try
        {
            ApplyDynamicConfigToYarp();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SaveDynamicConfig()
    {
        await _semaphore.WaitAsync();
        try
        {
            await PersistConfigToRepositoryAsync("SaveDynamicConfig");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task ReplaceAllConfig(
        IReadOnlyList<RouteConfig> newRoutes,
        IReadOnlyList<ClusterConfig> newClusters,
        string source = "rollback",
        string? createdBy = "dashboard-user")
    {
        await _semaphore.WaitAsync();
        try
        {
            _configProvider.Update(newRoutes, SanitizeClusters(newClusters));

            EnsureDynamicConfigInitialized();

            var existingClusters = _dynamicConfig!.Clusters.ToDictionary(
                c => c.ClusterId, c => c, StringComparer.OrdinalIgnoreCase);
            var existingRoutes = _dynamicConfig.Routes.ToDictionary(
                r => r.RouteId, r => r, StringComparer.OrdinalIgnoreCase);

            _dynamicConfig.Routes.Clear();
            _dynamicConfig.Clusters.Clear();

            foreach (var cluster in newClusters)
            {
                if (existingClusters.TryGetValue(cluster.ClusterId ?? string.Empty, out var existing))
                {
                    var dynCluster = existing;
                    dynCluster.Destinations = cluster.Destinations?.ToDictionary(
                        d => d.Key, d => d.Value.Address ?? string.Empty) ?? new Dictionary<string, string>();
                    dynCluster.LoadBalancingPolicy = cluster.LoadBalancingPolicy;
                    _dynamicConfig.Clusters.Add(dynCluster);
                }
                else
                {
                    _dynamicConfig.Clusters.Add(new DynamicClusterConfig
                    {
                        ClusterId = cluster.ClusterId ?? string.Empty,
                        Destinations = cluster.Destinations?.ToDictionary(
                            d => d.Key, d => d.Value.Address ?? string.Empty) ?? new Dictionary<string, string>(),
                        LoadBalancingPolicy = cluster.LoadBalancingPolicy ?? string.Empty,
                        Source = source,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = createdBy
                    });
                }
            }

            foreach (var route in newRoutes)
            {
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
                    _dynamicConfig.Routes.Add(new DynamicRouteConfig
                    {
                        RouteId = route.RouteId ?? string.Empty,
                        ClusterId = route.ClusterId ?? string.Empty,
                        MatchPath = route.Match?.Path ?? string.Empty,
                        Order = route.Order ?? 50,
                        Transforms = route.Transforms?.Select(t => new Dictionary<string, string>(t)).ToList(),
                        Source = source,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = createdBy
                    });
                }
            }

            _logger.LogInformation(
                "Configuration replaced: {Routes} routes, {Clusters} clusters",
                newRoutes.Count, newClusters.Count);
            _auditLog.RecordSuccess("RollbackConfig", "full config", createdBy, null,
                null,
                new { routeCount = newRoutes.Count, clusterCount = newClusters.Count });

            Interlocked.Increment(ref _configVersion);
            _dynamicConfig!.Version = _configVersion;
            await PersistConfigToRepositoryAsync("ReplaceAllConfig", "full config");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    // ── Heartbeat ────────────────────────────────────────────────────────

    public bool UpdateHeartbeat(string routeName, string? clientIp = null)
    {
        _semaphore.Wait();
        try
        {
            EnsureDynamicConfigInitialized();

            var route = _dynamicConfig!.Routes.FirstOrDefault(r =>
                r.RouteId.Equals(routeName, StringComparison.OrdinalIgnoreCase));

            if (route == null)
                return false;

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
            _semaphore.Release();
        }
    }

    // ── UpdateRouteMetadataAsync ─────────────────────────────────────────

    public async Task<bool> UpdateRouteMetadataAsync(string routeId, Dictionary<string, string> metadata)
    {
        if (string.IsNullOrWhiteSpace(routeId) || metadata.Count == 0)
            return false;

        bool saveNeeded = false;
        await _semaphore.WaitAsync();
        try
        {
            EnsureDynamicConfigInitialized();

            var route = _dynamicConfig!.Routes.FirstOrDefault(r =>
                r.RouteId.Equals(routeId, StringComparison.OrdinalIgnoreCase));

            if (route == null)
            {
                _logger.LogWarning("UpdateRouteMetadata: route '{RouteId}' not found", routeId);
                return false;
            }

            foreach (var kvp in metadata)
            {
                route.Metadata[kvp.Key] = kvp.Value;
            }

            saveNeeded = true;

            ApplyDynamicConfigToYarp();

            _logger.LogDebug(
                "Updated metadata for route '{RouteId}': {Keys}",
                routeId, string.Join(", ", metadata.Keys));
        }
        finally
        {
            if (saveNeeded)
            {
                Interlocked.Increment(ref _configVersion);
                _dynamicConfig!.Version = _configVersion;
                await PersistConfigToRepositoryAsync("UpdateRouteMetadata", routeId);
            }
            _semaphore.Release();
        }

        return true;
    }

    // ── UpdateClusterCircuitBreakerAsync ─────────────────────────────────

    public async Task<bool> UpdateClusterCircuitBreakerAsync(string clusterId, CircuitBreakerConfig? config)
    {
        if (string.IsNullOrWhiteSpace(clusterId))
            return false;

        bool saveNeeded = false;
        await _semaphore.WaitAsync();
        try
        {
            EnsureDynamicConfigInitialized();

            var dynCluster = _dynamicConfig!.Clusters.FirstOrDefault(c =>
                string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));

            if (dynCluster == null)
            {
                _logger.LogWarning("UpdateClusterCircuitBreaker: cluster '{ClusterId}' not found", clusterId);
                return false;
            }

            dynCluster.CircuitBreaker = config;
            saveNeeded = true;

            ApplyDynamicConfigToYarp();

            _logger.LogDebug(
                "Updated circuit breaker config for cluster '{ClusterId}': Enabled={Enabled}",
                clusterId, config?.Enabled ?? false);
        }
        finally
        {
            if (saveNeeded)
            {
                Interlocked.Increment(ref _configVersion);
                _dynamicConfig!.Version = _configVersion;
                await PersistConfigToRepositoryAsync("UpdateClusterCircuitBreaker", clusterId);
            }
            _semaphore.Release();
        }

        return true;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private void EnsureDynamicConfigInitialized()
    {
        if (_dynamicConfig == null)
        {
            _dynamicConfig = new GatewayDynamicConfig();
        }
    }

    private string? ResolveClusterUid(string? clusterId)
    {
        if (string.IsNullOrWhiteSpace(clusterId)) return null;
        return _dynamicConfig?.Clusters.FirstOrDefault(c =>
            string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase))?.ClusterUid;
    }

    private static global::Yarp.ReverseProxy.Configuration.HealthCheckConfig? BuildClusterHealthCheck(Models.HealthCheckConfig? config)
    {
        if (config == null) return null;

        return new global::Yarp.ReverseProxy.Configuration.HealthCheckConfig
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
    }
}
