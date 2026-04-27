using Aneiang.Yarp.Models;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services
{
    /// <summary>
    /// Dynamic YARP configuration management service.
    /// Supports incremental route and cluster updates — updates the route if the name exists,
    /// creates a new one otherwise.
    /// </summary>
    public class DynamicYarpConfigService
    {
        private readonly InMemoryConfigProvider _configProvider;
        private readonly ILogger<DynamicYarpConfigService> _logger;
        private readonly object _lock = new();

        /// <summary>
        /// Creates the dynamic configuration service.
        /// </summary>
        public DynamicYarpConfigService(
            InMemoryConfigProvider configProvider,
            ILogger<DynamicYarpConfigService> logger)
        {
            _configProvider = configProvider;
            _logger = logger;
        }

        /// <summary>
        /// Add or update a route. Updates the route if the name already exists,
        /// creates a new one otherwise.
        /// </summary>
        public (bool Success, string Message) TryAddRoute(RegisterRouteRequest request)
        {
            lock (_lock)
            {
                var config = _configProvider.GetConfig();
                var routes = config.Routes?.ToList() ?? new List<RouteConfig>();
                var clusters = config.Clusters?.ToList() ?? new List<ClusterConfig>();

                var newRoutes = new List<RouteConfig>(routes);
                var newClusters = new List<ClusterConfig>(clusters);

                var isNew = false;

                // Check if the route name already exists
                var existingRouteIndex = routes.FindIndex(r =>
                    string.Equals(r.RouteId, request.RouteName, StringComparison.OrdinalIgnoreCase));

                if (existingRouteIndex >= 0)
                {
                    newRoutes[existingRouteIndex] = new RouteConfig
                    {
                        RouteId = request.RouteName,
                        ClusterId = request.ClusterName,
                        Match = new RouteMatch { Path = request.MatchPath },
                        Order = request.Order ?? 50,
                    };
                    _logger.LogInformation("Route '{RouteName}' already exists, updating config", request.RouteName);
                }
                else
                {
                    newRoutes.Add(new RouteConfig
                    {
                        RouteId = request.RouteName,
                        ClusterId = request.ClusterName,
                        Match = new RouteMatch { Path = request.MatchPath },
                        Order = request.Order ?? 50,
                    });
                    isNew = true;
                    _logger.LogInformation("Route '{RouteName}' does not exist, adding new route", request.RouteName);
                }

                // Check if the cluster already exists; update its destination if so, create if not
                var existingClusterIndex = newClusters.FindIndex(c =>
                    string.Equals(c.ClusterId, request.ClusterName, StringComparison.OrdinalIgnoreCase));

                if (existingClusterIndex >= 0)
                {
                    newClusters[existingClusterIndex] = new ClusterConfig
                    {
                        ClusterId = request.ClusterName,
                        Destinations = new Dictionary<string, DestinationConfig>
                        {
                            ["d1"] = new DestinationConfig { Address = request.DestinationAddress }
                        }
                    };
                    _logger.LogInformation("Updating cluster '{ClusterName}' destination -> {Address}",
                        request.ClusterName, request.DestinationAddress);
                }
                else
                {
                    newClusters.Add(new ClusterConfig
                    {
                        ClusterId = request.ClusterName,
                        Destinations = new Dictionary<string, DestinationConfig>
                        {
                            ["d1"] = new DestinationConfig { Address = request.DestinationAddress }
                        }
                    });
                    _logger.LogInformation("Adding new cluster '{ClusterName}' -> {Address}",
                        request.ClusterName, request.DestinationAddress);
                }

                _configProvider.Update(newRoutes, newClusters);

                var action = isNew ? "Register" : "Update";
                _logger.LogInformation("Route '{RouteName}' {Action} success ({MatchPath} -> {Address})",
                    request.RouteName, action, request.MatchPath, request.DestinationAddress);
                return (true, $"Route '{request.RouteName}' {action} success");
            }
        }

        /// <summary>
        /// Delete a route. If the associated cluster is no longer referenced by any route,
        /// it will also be cleaned up.
        /// </summary>
        public (bool Success, string Message) TryRemoveRoute(string routeName)
        {
            if (string.IsNullOrWhiteSpace(routeName))
                return (false, "Route name cannot be empty");

            lock (_lock)
            {
                var config = _configProvider.GetConfig();
                var routes = config.Routes?.ToList() ?? new List<RouteConfig>();
                var clusters = config.Clusters?.ToList() ?? new List<ClusterConfig>();

                var route = routes.FirstOrDefault(r =>
                    string.Equals(r.RouteId, routeName, StringComparison.OrdinalIgnoreCase));

                if (route == null)
                    return (false, $"Route '{routeName}' does not exist");

                var clusterId = route.ClusterId;
                var newRoutes = routes.Where(r =>
                    !string.Equals(r.RouteId, routeName, StringComparison.OrdinalIgnoreCase)).ToList();

                // Check if the cluster is still referenced by other routes
                var isClusterReferenced = newRoutes.Any(r =>
                    string.Equals(r.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));

                List<ClusterConfig> newClusters;
                if (!isClusterReferenced && clusterId != null)
                {
                    // No remaining routes reference this cluster — remove it too
                    newClusters = clusters.Where(c =>
                        !string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase)).ToList();
                    _logger.LogInformation("Cluster '{ClusterId}' has no referenced routes, cleaning up", clusterId);
                }
                else
                {
                    newClusters = new List<ClusterConfig>(clusters);
                }

                _configProvider.Update(newRoutes, newClusters);
                _logger.LogInformation("Route '{RouteName}' deleted successfully", routeName);
                return (true, $"Route '{routeName}' deleted successfully");
            }
        }

        /// <summary>
        /// Get all registered route configurations.
        /// </summary>
        public IReadOnlyList<RouteConfig> GetRoutes()
        {
            var config = _configProvider.GetConfig();
            return config.Routes?.ToList().AsReadOnly() ?? new List<RouteConfig>().AsReadOnly();
        }

        /// <summary>
        /// Get all registered cluster configurations.
        /// </summary>
        public IReadOnlyList<ClusterConfig> GetClusters()
        {
            var config = _configProvider.GetConfig();
            return config.Clusters?.ToList().AsReadOnly() ?? new List<ClusterConfig>().AsReadOnly();
        }
    }
}
