using Aneiang.Yarp.Models;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Dynamic YARP config service: add, update, and delete routes and clusters at runtime.
/// Thread-safe with reader-writer lock protection.
/// </summary>
public class DynamicYarpConfigService
{
    private readonly InMemoryConfigProvider _configProvider;
    private readonly ILogger<DynamicYarpConfigService> _logger;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of DynamicYarpConfigService.
    /// </summary>
    /// <param name="configProvider">YARP in-memory config provider.</param>
    /// <param name="logger">Logger instance.</param>
    public DynamicYarpConfigService(InMemoryConfigProvider configProvider, ILogger<DynamicYarpConfigService> logger)
    {
        _configProvider = configProvider;
        _logger = logger;
    }

    /// <summary>
    /// Add or update a route (creates or replaces cluster). Thread-safe.
    /// </summary>
    /// <param name="request">Route registration request.</param>
    /// <returns>Route operation result.</returns>
    public RouteOperationResult TryAddRoute(RegisterRouteRequest request)
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
            _logger.LogInformation("Route '{RouteName}' deleted", routeName);
            return new RouteOperationResult(true, $"Route '{routeName}' deleted");
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
}
