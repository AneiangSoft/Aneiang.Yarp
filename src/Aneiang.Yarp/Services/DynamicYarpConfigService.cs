using Aneiang.Yarp.Models;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services
{
    /// <summary>
    /// 动态 YARP 配置管理服务
    /// 支持增量添加路由和集群，如果路由名已存在则更新，不存在则新增
    /// </summary>
    public class DynamicYarpConfigService
    {
        private readonly InMemoryConfigProvider _configProvider;
        private readonly ILogger<DynamicYarpConfigService> _logger;
        private readonly object _lock = new();

        /// <summary>
        /// 创建动态配置服务
        /// </summary>
        public DynamicYarpConfigService(
            InMemoryConfigProvider configProvider,
            ILogger<DynamicYarpConfigService> logger)
        {
            _configProvider = configProvider;
            _logger = logger;
        }

        /// <summary>
        /// 添加或更新路由，如果路由名称已存在则更新，不存在则新增
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

                // 检查路由名是否已存在
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
                    _logger.LogInformation("路由 '{RouteName}' 已存在，更新配置", request.RouteName);
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
                    _logger.LogInformation("路由 '{RouteName}' 不存在，新增路由", request.RouteName);
                }

                // 检查集群是否已存在，不存在则新建；存在则更新目标地址
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
                    _logger.LogInformation("更新集群 '{ClusterName}' 目标地址 -> {Address}",
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
                    _logger.LogInformation("新增集群 '{ClusterName}' -> {Address}",
                        request.ClusterName, request.DestinationAddress);
                }

                _configProvider.Update(newRoutes, newClusters);

                var action = isNew ? "注册" : "更新";
                _logger.LogInformation("路由 '{RouteName}' {Action}成功 ({MatchPath} -> {Address})",
                    request.RouteName, action, request.MatchPath, request.DestinationAddress);
                return (true, $"路由 '{request.RouteName}' {action}成功");
            }
        }

        /// <summary>
        /// 删除路由。如果删除后集群不再被任何路由引用，则同时清理集群。
        /// </summary>
        public (bool Success, string Message) TryRemoveRoute(string routeName)
        {
            if (string.IsNullOrWhiteSpace(routeName))
                return (false, "路由名称不能为空");

            lock (_lock)
            {
                var config = _configProvider.GetConfig();
                var routes = config.Routes?.ToList() ?? new List<RouteConfig>();
                var clusters = config.Clusters?.ToList() ?? new List<ClusterConfig>();

                var route = routes.FirstOrDefault(r =>
                    string.Equals(r.RouteId, routeName, StringComparison.OrdinalIgnoreCase));

                if (route == null)
                    return (false, $"路由 '{routeName}' 不存在");

                var clusterId = route.ClusterId;
                var newRoutes = routes.Where(r =>
                    !string.Equals(r.RouteId, routeName, StringComparison.OrdinalIgnoreCase)).ToList();

                // 检查该集群是否还被其他路由引用
                var isClusterReferenced = newRoutes.Any(r =>
                    string.Equals(r.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));

                List<ClusterConfig> newClusters;
                if (!isClusterReferenced && clusterId != null)
                {
                    // 无其他路由引用该集群，一并清理
                    newClusters = clusters.Where(c =>
                        !string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase)).ToList();
                    _logger.LogInformation("集群 '{ClusterId}' 已无关联路由，一并清理", clusterId);
                }
                else
                {
                    newClusters = new List<ClusterConfig>(clusters);
                }

                _configProvider.Update(newRoutes, newClusters);
                _logger.LogInformation("路由 '{RouteName}' 删除成功", routeName);
                return (true, $"路由 '{routeName}' 删除成功");
            }
        }

        /// <summary>
        /// 获取所有已注册的路由配置
        /// </summary>
        public IReadOnlyList<RouteConfig> GetRoutes()
        {
            var config = _configProvider.GetConfig();
            return config.Routes?.ToList().AsReadOnly() ?? new List<RouteConfig>().AsReadOnly();
        }

        /// <summary>
        /// 获取所有已注册的集群配置
        /// </summary>
        public IReadOnlyList<ClusterConfig> GetClusters()
        {
            var config = _configProvider.GetConfig();
            return config.Clusters?.ToList().AsReadOnly() ?? new List<ClusterConfig>().AsReadOnly();
        }
    }
}
