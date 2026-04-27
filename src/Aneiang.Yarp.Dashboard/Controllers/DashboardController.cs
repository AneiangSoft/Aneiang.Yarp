using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Yarp.ReverseProxy;

namespace Aneiang.Yarp.Dashboard.Controllers
{
    /// <summary>
    /// 网关维护仪表盘
    /// </summary>
    [Route("apigateway")]
    public class DashboardController : Controller
    {
        private readonly IProxyStateLookup _proxyState;
        private readonly IWebHostEnvironment _env;

        // 记录进程启动时间（UTC）
        private static readonly DateTime _startTime = DateTime.UtcNow;

        /// <summary>
        /// 创建仪表盘控制器
        /// </summary>
        public DashboardController(
            IProxyStateLookup proxyState,
            IWebHostEnvironment env)
        {
            _proxyState = proxyState;
            _env = env;
        }

        /// <summary>
        /// 仪表盘首页
        /// </summary>
        [HttpGet("")]
        public IActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// 获取网关基本信息
        /// </summary>
        [HttpGet("/apigateway/info")]
        public IActionResult GetInfo()
        {
            var process = Process.GetCurrentProcess();
            var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "未知";
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            var fileVersion = !string.IsNullOrEmpty(assemblyLocation)
                ? FileVersionInfo.GetVersionInfo(assemblyLocation).ProductVersion ?? assemblyVersion
                : assemblyVersion;
            var uptime = DateTime.UtcNow - _startTime;
            var memoryMb = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 1);
        
            return Json(new
            {
                code = 200,
                data = new
                {
                    version = fileVersion,
                    environment = _env.EnvironmentName,
                    startTime = _startTime.ToString("yyyy-MM-dd HH:mm:ss") + " (UTC)",
                    uptime = $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s",
                    memoryMb,
                    machineName = Environment.MachineName,
                    processId = process.Id
                }
            });
        }

        /// <summary>
        /// 获取 YARP 集群状态
        /// </summary>
        [HttpGet("/apigateway/clusters")]
        public IActionResult GetClusters()
        {
            var clusters = _proxyState.GetClusters().Select(cluster =>
            {
                var destinations = cluster.Destinations.Select(d => new
                {
                    name = d.Key,
                    address = d.Value.Model?.Config?.Address,
                    health = d.Value.Health.Active.ToString(),
                    passive = d.Value.Health.Passive.ToString()
                }).ToList();

                return new
                {
                    clusterId = cluster.ClusterId,
                    loadBalancingPolicy = cluster.Model?.Config?.LoadBalancingPolicy ?? "Default",
                    destinations,
                    healthyCount   = destinations.Count(d => d.health == "Healthy"),
                    unknownCount   = destinations.Count(d => d.health == "Unknown"),
                    unhealthyCount = destinations.Count(d => d.health == "Unhealthy"),
                    totalCount     = destinations.Count
                };
            }).ToList();

            return Json(new { code = 200, data = clusters });
        }

        /// <summary>
        /// 获取 YARP 路由配置
        /// </summary>
        [HttpGet("/apigateway/routes")]
        public IActionResult GetRoutes()
        {
            var routes = _proxyState.GetRoutes().Select(route => new
            {
                routeId = route.Config.RouteId,
                clusterId = route.Config.ClusterId,
                path = route.Config.Match.Path,
                methods = route.Config.Match.Methods,
                order = route.Config.Order
            }).OrderBy(r => r.order).ToList();

            return Json(new { code = 200, data = routes });
        }
    }
}
