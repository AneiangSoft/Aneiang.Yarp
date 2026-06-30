using Aneiang.Yarp.Models;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>
/// All route CRUD operations on the dynamic config working set. Thread-safe via a shared
/// <see cref="SemaphoreSlim"/> (shared with the cluster config manager).
/// </summary>
internal class RouteConfigManager : IRouteConfigManager
{
    private readonly AneiangProxyConfigProvider _configProvider;
    private readonly DynamicConfigState _state;
    private readonly SemaphoreSlim _semaphore;
    private readonly IDynamicConfigPersister _persister;
    private readonly IDynamicConfigPublisher _publisher;
    private readonly IConfigChangeAuditLog _auditLog;
    private readonly ILogger<RouteConfigManager> _logger;

    public RouteConfigManager(
        AneiangProxyConfigProvider configProvider,
        DynamicConfigState state,
        SemaphoreSlim semaphore,
        IDynamicConfigPersister persister,
        IDynamicConfigPublisher publisher,
        IConfigChangeAuditLog auditLog,
        ILogger<RouteConfigManager> logger)
    {
        _configProvider = configProvider;
        _state = state;
        _semaphore = semaphore;
        _persister = persister;
        _publisher = publisher;
        _auditLog = auditLog;
        _logger = logger;
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
                Order = request.Order ?? int.MaxValue,
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
                var oldRoute = newRoutes[existingRouteIdx];
                newRoutes[existingRouteIdx] = oldRoute with
                {
                    ClusterId = request.ClusterName,
                    Match = oldRoute.Match != null
                        ? new RouteMatch
                        {
                            Path = request.MatchPath,
                            Hosts = oldRoute.Match.Hosts,
                            Methods = oldRoute.Match.Methods,
                            Headers = oldRoute.Match.Headers,
                            QueryParameters = oldRoute.Match.QueryParameters
                        }
                        : new RouteMatch { Path = request.MatchPath },
                    Order = request.Order ?? int.MaxValue,
                    Transforms = request.Transforms?.Select(t => (IReadOnlyDictionary<string, string>)t).ToList() ?? oldRoute.Transforms
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

            // Cluster: create or update via helper
            if (request.UseIpIsolation && !string.IsNullOrWhiteSpace(request.ClientIp))
            {
                newClusters = ClusterEnsureHelper.EnsureIpCluster(
                    newClusters, request.ClusterName, request.ClientIp, request.DestinationAddress);
            }
            else
            {
                if (request.DestinationAddress is null or "")
                {
                    var exists = newClusters.Any(c =>
                        string.Equals(c.ClusterId, request.ClusterName, StringComparison.OrdinalIgnoreCase));
                    if (!exists)
                    {
                        _auditLog.RecordFailure("AddRoute", request.RouteName,
                            "Destination address is required when the cluster does not exist", createdBy);
                        return new RouteOperationResult(false,
                            "Destination address is required when the cluster does not exist");
                    }
                }
                else
                {
                    newClusters = ClusterEnsureHelper.EnsureNormalCluster(
                        newClusters, request.ClusterName, request.DestinationAddress);
                }
            }

            _state.EnsureInitialized();

            var dynRoute = _state.Config.Routes.FirstOrDefault(r =>
                string.Equals(r.Config.RouteId, request.RouteName, StringComparison.OrdinalIgnoreCase));

            var newRouteConfig = new RouteConfig
            {
                RouteId = request.RouteName,
                ClusterId = request.ClusterName,
                Match = new RouteMatch { Path = request.MatchPath },
                Order = request.Order ?? int.MaxValue,
                Transforms = request.Transforms?.Select(t => (IReadOnlyDictionary<string, string>)t).ToList()
            };

            if (dynRoute == null)
            {
                dynRoute = new DynamicRouteConfig
                {
                    Config = newRouteConfig,
                    ClusterUid = _state.ResolveClusterUid(request.ClusterName),
                    Source = source,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = createdBy
                };
                _state.Config.Routes.Add(dynRoute);
            }
            else
            {
                dynRoute.Config = newRouteConfig;
                dynRoute.ClusterUid = _state.ResolveClusterUid(request.ClusterName);
                if (!string.IsNullOrEmpty(source) && source != dynRoute.Source)
                {
                    dynRoute.Source = source;
                    dynRoute.CreatedBy = createdBy;
                }
            }

            var dynCluster = _state.Config.Clusters.FirstOrDefault(c =>
                string.Equals(c.Config.ClusterId, request.ClusterName, StringComparison.OrdinalIgnoreCase));

            if (dynCluster == null)
            {
                var destKey = request.UseIpIsolation && !string.IsNullOrWhiteSpace(request.ClientIp)
                    ? $"ip-{request.ClientIp.Replace(".", "-")}"
                    : "d1";
                var clusterConfig = new ClusterConfig
                {
                    ClusterId = request.ClusterName,
                    Destinations = new Dictionary<string, DestinationConfig>
                    {
                        [destKey] = new() { Address = request.DestinationAddress }
                    },
                    LoadBalancingPolicy = request.UseIpIsolation && !string.IsNullOrWhiteSpace(request.ClientIp) ? "IpBased" : null
                };
                dynCluster = new DynamicClusterConfig
                {
                    Config = clusterConfig,
                    Source = source,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = createdBy
                };
                _state.Config.Clusters.Add(dynCluster);
            }
            else if (!string.IsNullOrWhiteSpace(request.DestinationAddress) && !request.UseIpIsolation)
            {
                var dests = dynCluster.Config.Destinations?.ToDictionary(d => d.Key, d => d.Value)
                            ?? new Dictionary<string, DestinationConfig>();
                dests["d1"] = new DestinationConfig { Address = request.DestinationAddress };
                dynCluster.Config = dynCluster.Config with { Destinations = dests };
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
                _state.IncrementVersion();
                _publisher.Publish(_state.Config, _state.Version);
                await _persister.SaveAsync(_state.Config, "AddOrUpdateRoute", request.RouteName);
            }
            _semaphore.Release();
        }
    }

    // ── TryAddRouteConfig (full native config) ───────────────────────────

    public async Task<RouteOperationResult> TryAddRouteConfig(
        RouteConfig route,
        string source = "dashboard",
        string? createdBy = "dashboard-user")
    {
        if (string.IsNullOrWhiteSpace(route.RouteId))
            return new RouteOperationResult(false, "Route ID cannot be empty");
        if (string.IsNullOrWhiteSpace(route.ClusterId))
            return new RouteOperationResult(false, "Cluster ID cannot be empty");

        bool saveNeeded = false;
        await _semaphore.WaitAsync();
        try
        {
            var config = _configProvider.GetConfig();
            var newRoutes = new List<RouteConfig>(config.Routes ?? []);

            var existingRouteIdx = newRoutes.FindIndex(r =>
                string.Equals(r.RouteId, route.RouteId, StringComparison.OrdinalIgnoreCase));

            _state.EnsureInitialized();
            var dynRoute = _state.Config.Routes.FirstOrDefault(r =>
                string.Equals(r.Config.RouteId, route.RouteId, StringComparison.OrdinalIgnoreCase));

            var preservedMetadata = dynRoute?.Metadata ?? new Dictionary<string, string>();
            var effectiveRoute = route with
            {
                Metadata = DynamicYarpConfigHelpers.MergeRouteMetadata(route.Metadata, preservedMetadata)
            };

            bool isNew;
            if (existingRouteIdx >= 0)
            {
                newRoutes[existingRouteIdx] = effectiveRoute;
                isNew = false;
            }
            else
            {
                newRoutes.Add(effectiveRoute);
                isNew = true;
            }

            // Normalize comma-delimited transform values
            for (var ri = 0; ri < newRoutes.Count; ri++)
                newRoutes[ri] = DynamicYarpConfigHelpers.NormalizeTransforms(newRoutes[ri]);

            if (dynRoute == null)
            {
                dynRoute = new DynamicRouteConfig
                {
                    Config = route,
                    ClusterUid = _state.ResolveClusterUid(route.ClusterId),
                    Source = source,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = createdBy
                };
                _state.Config.Routes.Add(dynRoute);
            }
            else
            {
                dynRoute.Config = route;
                dynRoute.ClusterUid = _state.ResolveClusterUid(route.ClusterId);
                if (!string.IsNullOrEmpty(source) && source != dynRoute.Source)
                {
                    dynRoute.Source = source;
                    dynRoute.CreatedBy = createdBy;
                }
            }

            saveNeeded = true;

            var action = isNew ? "registered" : "updated";
            _logger.LogInformation("Route '{RouteId}' {Action} via full config", route.RouteId, action);
            _auditLog.RecordSuccess(
                isNew ? "AddRoute" : "UpdateRoute",
                route.RouteId,
                createdBy, null,
                new { clusterId = route.ClusterId },
                new { clusterId = route.ClusterId, matchPath = route.Match?.Path, order = route.Order });
            return new RouteOperationResult(true, $"Route '{route.RouteId}' {action}");
        }
        finally
        {
            if (saveNeeded)
            {
                _state.IncrementVersion();
                _publisher.Publish(_state.Config, _state.Version);
                await _persister.SaveAsync(_state.Config, "AddOrUpdateRoute", route.RouteId);
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
                var (resultClusters, destKey, clusterRemoved, found) = ClusterEnsureHelper.RemoveIpDestination(
                    clusters.ToList(), clusterId, clientIp);

                if (!found)
                    return new RouteOperationResult(false,
                        clusterRemoved ? "Cluster removed (no destinations left)" : $"Cluster '{clusterId}' not found");

                _state.EnsureInitialized();
                if (clusterRemoved)
                {
                    _state.Config.Routes.RemoveAll(r =>
                        string.Equals(r.Config.RouteId, routeName, StringComparison.OrdinalIgnoreCase));
                    _state.Config.Clusters.RemoveAll(c =>
                        string.Equals(c.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    var dynCluster = _state.Config.Clusters.FirstOrDefault(c =>
                        string.Equals(c.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
                    if (dynCluster != null)
                    {
                        var dests = dynCluster.Config.Destinations?.ToDictionary(d => d.Key, d => d.Value)
                                    ?? new Dictionary<string, DestinationConfig>();
                        dests.Remove(destKey);
                        dynCluster.Config = dynCluster.Config with { Destinations = dests };
                    }
                }

                saveNeeded = true;
                _logger.LogDebug(
                    "IP isolation: removed destination '{DestKey}' from cluster '{ClusterId}' (client IP: {ClientIp})",
                    destKey, clusterId, clientIp);
                return new RouteOperationResult(true,
                    clusterRemoved
                        ? $"Cluster '{clusterId}' removed (no destinations left after IP removal)"
                        : $"Destination for IP '{clientIp}' removed from cluster '{clusterId}'");
            }

            // Normal: delete route and optionally the orphaned cluster
            var newRoutes2 = new List<RouteConfig>(routes.Where(r =>
                !string.Equals(r.RouteId, routeName, StringComparison.OrdinalIgnoreCase)));

            var orphaned = clusterId != null && !newRoutes2.Any(r =>
                string.Equals(r.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));

            _state.EnsureInitialized();
            _state.Config.Routes.RemoveAll(r =>
                string.Equals(r.Config.RouteId, routeName, StringComparison.OrdinalIgnoreCase));

            if (orphaned && removeOrphanedCluster && clusterId != null)
            {
                _state.Config.Clusters.RemoveAll(c =>
                    string.Equals(c.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
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
                _state.IncrementVersion();
                _publisher.Publish(_state.Config, _state.Version);
                await _persister.SaveAsync(_state.Config, "RemoveRoute", routeName);
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

            var newRoute = oldRoute with
            {
                RouteId = newRouteId,
                ClusterId = request.ClusterName ?? oldRoute.ClusterId,
                Match = request.MatchPath != null
                    ? new RouteMatch
                    {
                        Path = request.MatchPath,
                        Hosts = oldRoute.Match?.Hosts,
                        Methods = oldRoute.Match?.Methods,
                        Headers = oldRoute.Match?.Headers,
                        QueryParameters = oldRoute.Match?.QueryParameters
                    }
                    : oldRoute.Match,
                Order = request.Order ?? oldRoute.Order,
                Transforms = request.Transforms?.Select(t => (IReadOnlyDictionary<string, string>)t).ToList()
                    ?? oldRoute.Transforms
            };
            newRoutes.Add(newRoute);

            newRoutes.RemoveAll(r =>
                string.Equals(r.RouteId, oldRouteId, StringComparison.OrdinalIgnoreCase));

            _state.EnsureInitialized();

            var dynRoute = _state.Config.Routes.FirstOrDefault(r =>
                string.Equals(r.Config.RouteId, oldRouteId, StringComparison.OrdinalIgnoreCase));
            if (dynRoute != null)
            {
                var renamedDynRoute = new DynamicRouteConfig
                {
                    Config = newRoute,
                    RouteUid = dynRoute.RouteUid,
                    ClusterUid = _state.ResolveClusterUid(request.ClusterName ?? newRoute.ClusterId),
                    Source = source,
                    CreatedAt = dynRoute.CreatedAt,
                    CreatedBy = createdBy,
                    Metadata = new Dictionary<string, string>(dynRoute.Metadata)
                };
                _state.Config.Routes.RemoveAll(r =>
                    string.Equals(r.Config.RouteId, oldRouteId, StringComparison.OrdinalIgnoreCase));
                _state.Config.Routes.Add(renamedDynRoute);
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
                _state.IncrementVersion();
                _publisher.Publish(_state.Config, _state.Version);
                await _persister.SaveAsync(_state.Config, "UpdateRoute", oldRouteId);
            }
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
            _state.EnsureInitialized();

            var route = _state.Config.Routes.FirstOrDefault(r =>
                (r.Config.RouteId ?? string.Empty).Equals(routeId, StringComparison.OrdinalIgnoreCase));

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

            _publisher.Publish(_state.Config, _state.Version);

            _logger.LogDebug(
                "Updated metadata for route '{RouteId}': {Keys}",
                routeId, string.Join(", ", metadata.Keys));
        }
        finally
        {
            if (saveNeeded)
            {
                _state.IncrementVersion();
                _publisher.Publish(_state.Config, _state.Version);
                await _persister.SaveAsync(_state.Config, "UpdateRouteMetadata", routeId);
            }
            _semaphore.Release();
        }

        return true;
    }

    // ── Query methods ────────────────────────────────────────────────────

    public IReadOnlyList<RouteConfig> GetRoutes()
    {
        _semaphore.Wait();
        try
        {
            return _configProvider.GetConfig().Routes ?? Array.Empty<RouteConfig>();
        }
        finally { _semaphore.Release(); }
    }

    public GatewayDynamicConfig GetDynamicConfig() => _state.Config;
}
