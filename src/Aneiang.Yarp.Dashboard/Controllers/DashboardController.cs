using System.Diagnostics;
using System.Reflection;
using Aneiang.Yarp.Dashboard.Extensions;
using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Dashboard.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Yarp.ReverseProxy;

namespace Aneiang.Yarp.Dashboard.Controllers
{
    /// <summary>
    /// Gateway maintenance dashboard.
    /// </summary>
    public class DashboardController : Controller
    {
        /// <summary>
        /// Route prefix for dashboard pages and APIs.
        /// Modified by <c>DashboardRouteConvention</c> during application model building.
        /// </summary>
        internal static string RoutePrefix { get; set; } = "apigateway";

        /// <summary>
        /// JWT configuration (set by convention at startup).
        /// </summary>
        internal static DashboardOptions Options { get; set; } = new();

        private readonly IProxyStateLookup _proxyState;
        private readonly IWebHostEnvironment _env;
        private readonly ProxyLogStore _logStore;

        // Record process start time (UTC) and cache assembly version
        private static readonly DateTime _startTime = DateTime.UtcNow;
        private static readonly string _fileVersion;

        static DashboardController()
        {
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            _fileVersion = !string.IsNullOrEmpty(assemblyLocation)
                ? FileVersionInfo.GetVersionInfo(assemblyLocation).ProductVersion
                  ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown"
                : Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
        }

        /// <summary>
        /// Creates the dashboard controller.
        /// </summary>
        public DashboardController(
            IProxyStateLookup proxyState,
            IWebHostEnvironment env,
            ProxyLogStore logStore)
        {
            _proxyState = proxyState;
            _env = env;
            _logStore = logStore;
        }

        /// <summary>
        /// Dashboard home page.
        /// </summary>
        [HttpGet("")]
        public IActionResult Index()
        {
            ViewBag.DashboardRoutePrefix = RoutePrefix;
            return View();
        }

        /// <summary>
        /// Login page.
        /// </summary>
        [HttpGet("login")]
        public IActionResult Login()
        {
            ViewBag.DashboardRoutePrefix = RoutePrefix;
            ViewBag.AuthMode = Options.AuthMode;
            return View();
        }

        /// <summary>
        /// Login POST - validate credentials and return JWT token.
        /// </summary>
        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return Json(new { code = 400, message = "Username and password are required" });

            bool valid = Options.AuthMode switch
            {
                DashboardAuthMode.CustomJwt =>
                    request.Username == Options.JwtUsername && request.Password == Options.JwtPassword,
                DashboardAuthMode.DefaultJwt =>
                    request.Username == "admin" && request.Password == Options.JwtPassword,
                _ => false
            };

            if (!valid)
                return Json(new { code = 401, message = "Invalid credentials" });

            var token = DashboardJwtHelper.GenerateToken(request.Username, Options.JwtSecret!);

            // Set HttpOnly cookie for page navigation
            Response.Cookies.Append("dashboard_token", token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddHours(8)
            });

            return Json(new { code = 200, token });
        }

        /// <summary>
        /// Get gateway basic information.
        /// </summary>
        [HttpGet("info")]
        public IActionResult GetInfo()
        {
            var process = Process.GetCurrentProcess();
            var uptime = DateTime.UtcNow - _startTime;
            var memoryMb = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 1);

            return Json(new
            {
                code = 200,
                data = new
                {
                    version = _fileVersion,
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
        [HttpGet("clusters")]
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
                    healthyCount = destinations.Count(d => d.health == "Healthy"),
                    unknownCount = destinations.Count(d => d.health == "Unknown"),
                    unhealthyCount = destinations.Count(d => d.health == "Unhealthy"),
                    totalCount = destinations.Count
                };
            }).ToList();

            return Json(new { code = 200, data = clusters });
        }

        /// <summary>
        /// Get YARP route configuration.
        /// </summary>
        [HttpGet("routes")]
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

        /// <summary>
        /// Get recent YARP proxy log entries.
        /// </summary>
        /// <param name="count">Maximum number of entries to return (default 100).</param>
        [HttpGet("logs")]
        public IActionResult GetLogs([FromQuery] int count = 100)
        {
            var snapshot = _logStore.GetRecent(count);
            return Json(new { code = 200, data = snapshot });
        }

        /// <summary>
        /// Clear all stored log entries.
        /// </summary>
        [HttpDelete("logs")]
        public IActionResult ClearLogs()
        {
            _logStore.Clear();
            return Json(new { code = 200, message = "Logs cleared" });
        }
    }

    /// <summary>Login credentials for JWT authentication.</summary>
    public class LoginRequest
    {
        /// <summary>Username.</summary>
        public string Username { get; set; } = string.Empty;
        /// <summary>Password.</summary>
        public string Password { get; set; } = string.Empty;
    }
}
