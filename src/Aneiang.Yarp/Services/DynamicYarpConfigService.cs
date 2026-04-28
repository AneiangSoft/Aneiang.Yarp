using Aneiang.Yarp.Models;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>Dynamic YARP config service: add/update/delete routes and clusters at runtime / YARP 动态配置服务：运行时增删改路由和集群.</summary>
public class DynamicYarpConfigService
{
    private readonly InMemoryConfigProvider _configProvider;
    private readonly ILogger<DynamicYarpConfigService> _logger;
    private readonly object _lock = new();

    /// <summary>Initializes a new instance of DynamicYarpConfigService / 初始化实例.</summary>
    /// <param name="configProvider">YARP in-memory config provider / YARP 内存配置提供程序.</param>
    /// <param name="logger">Logger instance / 日志记录器.</param>
    public DynamicYarpConfigService(InMemoryConfigProvider configProvider, ILogger<DynamicYarpConfigService> logger)
    {
        _configProvider = configProvider;
        _logger = logger;
    }

    /// <summary>Add or update a route (creates/replaces cluster). Thread-safe / 添加或更新路由（同时创建/替换集群），线程安全.</summary>
    public (bool Success, string Message) TryAddRoute(RegisterRouteRequest request)
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
            return (true, $"Route '{request.RouteName}' {action}");
        }
    }

    /// <summary>Delete a route. Also removes cluster if no remaining routes reference it / 删除路由，集群无引用时一并清除.</summary>
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
                return (false, $"Route '{routeName}' not found");

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
            return (true, $"Route '{routeName}' deleted");
        }
    }

    /// <summary>Get all routes / 获取所有路由.</summary>
    public IReadOnlyList<RouteConfig> GetRoutes() => _configProvider.GetConfig().Routes ?? Array.Empty<RouteConfig>();

    /// <summary>Get all clusters / 获取所有集群.</summary>
    public IReadOnlyList<ClusterConfig> GetClusters() => _configProvider.GetConfig().Clusters ?? Array.Empty<ClusterConfig>();
}
