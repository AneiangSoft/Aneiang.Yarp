using Aneiang.Yarp.Models;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>
/// All route CRUD operations on the dynamic config working set. Thread-safe via a shared
/// <see cref="SemaphoreSlim"/> (shared with the cluster config manager).
/// Lock/publish/persist plumbing is delegated to <see cref="ConfigManagerBase"/>.
/// </summary>
internal class RouteConfigManager : ConfigManagerBase, IRouteConfigManager
{
    private readonly ILogger<RouteConfigManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RouteConfigManager"/> class.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="semaphore">The semaphore.</param>
    /// <param name="persister">The persister.</param>
    /// <param name="publisher">The publisher.</param>
    /// <param name="auditLog">The audit log.</param>
    /// <param name="logger">The logger.</param>
    public RouteConfigManager(
        DynamicConfigState state,
        SemaphoreSlim semaphore,
        IDynamicConfigPersister persister,
        IDynamicConfigPublisher publisher,
        IConfigChangeAuditLog auditLog,
        ILogger<RouteConfigManager> logger)
        : base(state, semaphore, persister, publisher, auditLog)
    {
        _logger = logger;
    }

    protected override void LogPersistError(Exception ex, string operationName, string? targetName)
    {
        _logger.LogError(ex, "Failed to persist route operation '{Operation}' for '{Target}'",
            operationName, targetName);
    }

    #region TryAddRoute

    /// <summary>
    /// Tries the add route.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="source">The source.</param>
    /// <param name="createdBy">The created by.</param>
    /// <returns>A Task.</returns>
    public async Task<RouteOperationResult> TryAddRoute(
    RegisterRouteRequest request,
    string source = "dynamic",
    string? createdBy = null)
    {
        return await ExecuteWithLockAsync("AddOrUpdateRoute", request.RouteName, config =>
        {
            var routeConfig = new RouteConfig
            {
                RouteId = request.RouteName,
                ClusterId = request.ClusterName,
                Match = new RouteMatch { Path = request.MatchPath },
                Order = request.Order ?? int.MaxValue,
                Transforms = request.Transforms?.Select(t => (IReadOnlyDictionary<string, string>)t).ToList()
            };

            var dynRoute = config.Routes.FirstOrDefault(r =>
                string.Equals(r.Config.RouteId, request.RouteName, StringComparison.OrdinalIgnoreCase));

            bool isNew;
            if (dynRoute == null)
            {
                dynRoute = new DynamicRouteConfig
                {
                    Config = routeConfig,
                    ClusterUid = State.ResolveClusterUid(request.ClusterName),
                    Source = source,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = createdBy
                };
                config.Routes.Add(dynRoute);
                isNew = true;
            }
            else
            {
                dynRoute.Config = routeConfig;
                dynRoute.ClusterUid = State.ResolveClusterUid(request.ClusterName);
                if (!string.IsNullOrEmpty(source) && source != dynRoute.Source)
                {
                    dynRoute.Source = source;
                    dynRoute.CreatedBy = createdBy;
                }
                isNew = false;
            }

            // Cluster: create or update
            var dynCluster = config.Clusters.FirstOrDefault(c =>
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
                config.Clusters.Add(dynCluster);
            }
            else if (!string.IsNullOrWhiteSpace(request.DestinationAddress) && !request.UseIpIsolation)
            {
                var dests = dynCluster.Config.Destinations?.ToDictionary(d => d.Key, d => d.Value)
                            ?? new Dictionary<string, DestinationConfig>();
                dests["d1"] = new DestinationConfig { Address = request.DestinationAddress };
                dynCluster.Config = dynCluster.Config with { Destinations = dests };
            }

            var action = isNew ? "registered" : "updated";
            _logger.LogInformation("Route '{RouteName}' {Action} ({MatchPath} -> {Address})",
                request.RouteName, action, request.MatchPath, request.DestinationAddress);
            AuditLog.RecordSuccess(
                isNew ? "AddRoute" : "UpdateRoute",
                request.RouteName,
                createdBy, null,
                new { clusterId = request.ClusterName, matchPath = request.MatchPath },
                new { clusterId = request.ClusterName, matchPath = request.MatchPath, destination = request.DestinationAddress });
            return Task.FromResult(new RouteOperationResult(true, $"Route '{request.RouteName}' {action}"));
        });
    }

    #endregion

    #region TryAddRouteConfig (full native config)

    /// <summary>
    /// Tries the add route config.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <param name="source">The source.</param>
    /// <param name="createdBy">The created by.</param>
    /// <returns>A Task.</returns>
    public async Task<RouteOperationResult> TryAddRouteConfig(
    RouteConfig route,
    string source = "dashboard",
    string? createdBy = "dashboard-user")
    {
        if (string.IsNullOrWhiteSpace(route.RouteId))
            return new RouteOperationResult(false, "Route ID cannot be empty");
        if (string.IsNullOrWhiteSpace(route.ClusterId))
            return new RouteOperationResult(false, "Cluster ID cannot be empty");

        return await ExecuteWithLockAsync("AddOrUpdateRoute", route.RouteId, config =>
        {
            var dynRoute = config.Routes.FirstOrDefault(r =>
                string.Equals(r.Config.RouteId, route.RouteId, StringComparison.OrdinalIgnoreCase));

            var preservedMetadata = dynRoute?.Metadata ?? new Dictionary<string, string>();
            var effectiveRoute = route with
            {
                Metadata = DynamicYarpConfigHelpers.MergeRouteMetadata(route.Metadata, preservedMetadata)
            };

            bool isNew;
            if (dynRoute == null)
            {
                dynRoute = new DynamicRouteConfig
                {
                    Config = effectiveRoute,
                    ClusterUid = State.ResolveClusterUid(route.ClusterId),
                    Source = source,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = createdBy
                };
                config.Routes.Add(dynRoute);
                isNew = true;
            }
            else
            {
                dynRoute.Config = effectiveRoute;
                dynRoute.ClusterUid = State.ResolveClusterUid(route.ClusterId);
                if (!string.IsNullOrEmpty(source) && source != dynRoute.Source)
                {
                    dynRoute.Source = source;
                    dynRoute.CreatedBy = createdBy;
                }
                isNew = false;
            }

            // Normalize comma-delimited transform values
            for (var ri = 0; ri < config.Routes.Count; ri++)
                config.Routes[ri].Config = DynamicYarpConfigHelpers.NormalizeTransforms(config.Routes[ri].Config);

            var action = isNew ? "registered" : "updated";
            _logger.LogInformation("Route '{RouteId}' {Action} via full config", route.RouteId, action);
            AuditLog.RecordSuccess(
                isNew ? "AddRoute" : "UpdateRoute",
                route.RouteId,
                createdBy, null,
                new { clusterId = route.ClusterId },
                new { clusterId = route.ClusterId, matchPath = route.Match?.Path, order = route.Order });
            return Task.FromResult(new RouteOperationResult(true, $"Route '{route.RouteId}' {action}"));
        });
    }


    #endregion

    #region TryRemoveRoute

    /// <summary>
    /// Tries the remove route.
    /// </summary>
    /// <param name="routeName">The route name.</param>
    /// <param name="clientIp">The client ip.</param>
    /// <param name="removeOrphanedCluster">If true, remove orphaned cluster.</param>
    /// <returns>A Task.</returns>
    public async Task<RouteOperationResult> TryRemoveRoute(string routeName, string? clientIp = null, bool removeOrphanedCluster = true)
    {
        if (string.IsNullOrWhiteSpace(routeName))
            return new RouteOperationResult(false, "Route name cannot be empty");

        return await ExecuteWithLockAsync("RemoveRoute", routeName, config =>
        {
            var dynRoute = config.Routes.FirstOrDefault(r =>
                string.Equals(r.Config.RouteId, routeName, StringComparison.OrdinalIgnoreCase));
            if (dynRoute == null)
            {
                AuditLog.RecordFailure("RemoveRoute", routeName, $"Route '{routeName}' not found");
                return Task.FromResult(new RouteOperationResult(false, $"Route '{routeName}' not found"));
            }

            var clusterId = dynRoute.Config.ClusterId;

            // IP isolation: only remove the matching destination
            if (!string.IsNullOrWhiteSpace(clientIp) && clusterId != null)
            {
                var dynCluster = config.Clusters.FirstOrDefault(c =>
                    string.Equals(c.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
                if (dynCluster != null)
                {
                    var destKey = $"ip-{clientIp.Replace(".", "-")}";
                    var dests = dynCluster.Config.Destinations?.ToDictionary(d => d.Key, d => d.Value)
                                ?? new Dictionary<string, DestinationConfig>();
                    if (dests.Remove(destKey))
                    {
                        if (dests.Count == 0)
                        {
                            config.Routes.RemoveAll(r =>
                                string.Equals(r.Config.RouteId, routeName, StringComparison.OrdinalIgnoreCase));
                            config.Clusters.RemoveAll(c =>
                                string.Equals(c.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
                            _logger.LogDebug(
                                "IP isolation: removed last destination from cluster '{ClusterId}', cluster removed",
                                clusterId);
                            return Task.FromResult(new RouteOperationResult(true,
                                $"Cluster '{clusterId}' removed (no destinations left after IP removal)"));
                        }
                        dynCluster.Config = dynCluster.Config with { Destinations = dests };
                    }
                }

                config.Routes.RemoveAll(r =>
                    string.Equals(r.Config.RouteId, routeName, StringComparison.OrdinalIgnoreCase));
                _logger.LogDebug(
                    "IP isolation: removed route '{RouteName}' and destination for IP '{ClientIp}' from cluster '{ClusterId}'",
                    routeName, clientIp, clusterId);
                return Task.FromResult(new RouteOperationResult(true,
                    $"Destination for IP '{clientIp}' removed from cluster '{clusterId}'"));
            }

            // Normal: delete route and optionally the orphaned cluster
            config.Routes.RemoveAll(r =>
                string.Equals(r.Config.RouteId, routeName, StringComparison.OrdinalIgnoreCase));

            if (removeOrphanedCluster && clusterId != null)
            {
                var orphaned = !config.Routes.Any(r =>
                    string.Equals(r.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
                if (orphaned)
                {
                    config.Clusters.RemoveAll(c =>
                        string.Equals(c.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
                }
            }

            _logger.LogInformation("Route '{RouteName}' deleted", routeName);
            AuditLog.RecordSuccess("RemoveRoute", routeName, null, null,
                new { clusterId, matchPath = dynRoute.Config.Match?.Path });
            return Task.FromResult(new RouteOperationResult(true, $"Route '{routeName}' deleted"));
        });
    }


    #endregion


    #region TryRenameRoute

    /// <summary>
    /// Tries the rename route.
    /// </summary>
    /// <param name="oldRouteId">The old route id.</param>
    /// <param name="newRouteId">The new route id.</param>
    /// <param name="request">The request.</param>
    /// <param name="source">The source.</param>
    /// <param name="createdBy">The created by.</param>
    /// <returns>A Task.</returns>
    public async Task<RouteOperationResult> TryRenameRoute(
        string oldRouteId,
        string newRouteId,
        RegisterRouteRequest request,
        string source = "dashboard",
        string? createdBy = "dashboard-user")
    {
        if (string.IsNullOrWhiteSpace(oldRouteId) || string.IsNullOrWhiteSpace(newRouteId))
        {
            AuditLog.RecordFailure("RenameRoute", oldRouteId ?? "", "Route ID cannot be empty");
            return new RouteOperationResult(false, "Route ID cannot be empty");
        }

        if (string.Equals(oldRouteId, newRouteId, StringComparison.OrdinalIgnoreCase))
        {
            return new RouteOperationResult(false, "Old and new route IDs are the same");
        }

        return await ExecuteWithLockAsync("UpdateRoute", oldRouteId, config =>
        {
            var dynRoute = config.Routes.FirstOrDefault(r =>
                string.Equals(r.Config.RouteId, oldRouteId, StringComparison.OrdinalIgnoreCase));
            if (dynRoute == null)
            {
                AuditLog.RecordFailure("UpdateRoute", oldRouteId, $"Route '{oldRouteId}' not found");
                return Task.FromResult(new RouteOperationResult(false, $"Route '{oldRouteId}' not found"));
            }

            if (config.Routes.Any(r =>
                string.Equals(r.Config.RouteId, newRouteId, StringComparison.OrdinalIgnoreCase)))
            {
                AuditLog.RecordFailure("UpdateRoute", oldRouteId,
                    $"Target route '{newRouteId}' already exists");
                return Task.FromResult(new RouteOperationResult(false, $"Target route '{newRouteId}' already exists"));
            }

            var oldConfig = dynRoute.Config;
            var newConfig = oldConfig with
            {
                RouteId = newRouteId,
                ClusterId = request.ClusterName ?? oldConfig.ClusterId,
                Match = request.MatchPath != null
                    ? new RouteMatch
                    {
                        Path = request.MatchPath,
                        Hosts = oldConfig.Match?.Hosts,
                        Methods = oldConfig.Match?.Methods,
                        Headers = oldConfig.Match?.Headers,
                        QueryParameters = oldConfig.Match?.QueryParameters
                    }
                    : oldConfig.Match,
                Order = request.Order ?? oldConfig.Order,
                Transforms = request.Transforms?.Select(t => (IReadOnlyDictionary<string, string>)t).ToList()
                    ?? oldConfig.Transforms
            };

            var renamedDynRoute = new DynamicRouteConfig
            {
                Config = newConfig,
                RouteUid = dynRoute.RouteUid,
                ClusterUid = State.ResolveClusterUid(request.ClusterName ?? newConfig.ClusterId),
                Source = source,
                CreatedAt = dynRoute.CreatedAt,
                CreatedBy = createdBy,
                Metadata = new Dictionary<string, string>(dynRoute.Metadata)
            };
            config.Routes.RemoveAll(r =>
                string.Equals(r.Config.RouteId, oldRouteId, StringComparison.OrdinalIgnoreCase));
            config.Routes.Add(renamedDynRoute);

            _logger.LogInformation(
                "Route '{OldRouteId}' renamed to '{NewRouteId}'",
                oldRouteId, newRouteId);
            AuditLog.RecordSuccess("UpdateRoute", $"{oldRouteId} → {newRouteId}", createdBy, null,
                new { oldRouteId, action = "rename" },
                new { newRouteId, clusterId = request.ClusterName, matchPath = request.MatchPath, action = "rename" });
            return Task.FromResult(new RouteOperationResult(true,
                $"Route '{oldRouteId}' renamed to '{newRouteId}'"));
        });
    }

    #endregion

    #region UpdateRouteMetadataAsync

    /// <summary>
    /// Updates the route metadata async.
    /// </summary>
    /// <param name="routeId">The route id.</param>
    /// <param name="metadata">The metadata.</param>
    /// <returns>A Task.</returns>
    public async Task<bool> UpdateRouteMetadataAsync(string routeId, Dictionary<string, string> metadata)
    {
        if (string.IsNullOrWhiteSpace(routeId) || metadata.Count == 0)
            return false;

        return await ExecuteMetadataWithLockAsync("UpdateRouteMetadata", routeId, config =>
        {
            var route = config.Routes.FirstOrDefault(r =>
                (r.Config.RouteId ?? string.Empty).Equals(routeId, StringComparison.OrdinalIgnoreCase));

            if (route == null)
            {
                _logger.LogWarning("UpdateRouteMetadata: route '{RouteId}' not found", routeId);
                return Task.FromResult(false);
            }

            foreach (var kvp in metadata)
            {
                route.Metadata[kvp.Key] = kvp.Value;
            }

            _logger.LogDebug(
                "Updated metadata for route '{RouteId}': {Keys}",
                routeId, string.Join(", ", metadata.Keys));
            return Task.FromResult(true);
        });
    }

    #endregion

    #region Query methods

    /// <summary>
    /// Gets the routes.
    /// </summary>
    /// <returns>A list of RouteConfigs.</returns>
    public IReadOnlyList<RouteConfig> GetRoutes()
    {
        // Direct read from volatile snapshot — no lock needed.
        return State.Config.Routes?.Select(r => r.Config).ToList() ?? [];
    }

    /// <summary>
    /// Gets the dynamic config.
    /// </summary>
    /// <returns>A GatewayDynamicConfig.</returns>
    public GatewayDynamicConfig GetDynamicConfig() => State.Config;

    #endregion
}
