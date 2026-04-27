using Aneiang.Yarp.Dashboard.Models;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Dashboard.Services
{
    /// <summary>
    /// 动态 YARP 配置管理服务
    /// 支持增量添加路由和集群，如果路由名已存在则更新，不存在则新增
    /// </summary>
    public class DynamicYarpConfigService
    {
        private readonly InMemoryConfigProvider _configProvider;
        private readonly ILogger<DynamicYarpConfigService> _logger;

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
                // 更新已有路由
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
                // 新增路由
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
                // 更新已有集群的目标地址
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
                // 新增集群
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

            // 更新 InMemoryConfigProvider，触发 YARP 热重载
            _configProvider.Update(newRoutes, newClusters);

            var action = isNew ? "注册" : "更新";
            _logger.LogInformation("路由 '{RouteName}' {Action}成功 ({MatchPath} -> {Address})",
                request.RouteName, action, request.MatchPath, request.DestinationAddress);
            return (true, $"路由 '{request.RouteName}' {action}成功");
        }
    }
}
