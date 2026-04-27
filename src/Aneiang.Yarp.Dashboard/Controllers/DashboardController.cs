using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Yarp.ReverseProxy;

namespace Aneiang.Yarp.Dashboard.Controllers
{
    /// <summary>
    /// Gateway maintenance dashboard.
    /// </summary>
    [Route("apigateway")]
    public class DashboardController : Controller
    {
        private readonly IProxyStateLookup _proxyState;
        private readonly IWebHostEnvironment _env;

        // Record process start time (UTC)
        private static readonly DateTime _startTime = DateTime.UtcNow;

        /// <summary>
        /// Creates the dashboard controller.
        /// </summary>
        public DashboardController(
            IProxyStateLookup proxyState,
            IWebHostEnvironment env)
        {
            _proxyState = proxyState;
            _env = env;
        }

        /// <summary>
        /// Dashboard home page.
        /// </summary>
        [HttpGet("")]
        public IActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Get gateway basic information.
        /// </summary>
        [HttpGet("/apigateway/info")]
        public IActionResult GetInfo()
        {
            var process = Process.GetCurrentProcess();
            var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
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
        /// Get YARP cluster status.
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
        /// Get YARP route configuration.
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
