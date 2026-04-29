using System.Diagnostics;
using System.Reflection;
using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Dashboard.Services;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Dashboard.Controllers;

/// <summary>Gateway maintenance dashboard.</summary>
public class DashboardController : Controller
{
    /// <summary>Route prefix, set by convention at startup.</summary>
    internal static string RoutePrefix { get; set; } = "apigateway";

    private readonly IProxyStateLookup _proxyState;
    private readonly IWebHostEnvironment _env;
    private readonly ProxyLogStore _logStore;
    private readonly DynamicYarpConfigService _dynamicConfig;
    private readonly DashboardOptions _options;

    private static readonly DateTime _startTime = DateTime.Now;
    private static readonly string _fileVersion;

    static DashboardController()
    {
        var location = Assembly.GetExecutingAssembly().Location;
        _fileVersion = !string.IsNullOrEmpty(location)
            ? FileVersionInfo.GetVersionInfo(location).ProductVersion
              ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown"
            : Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
    }

    /// <summary>Initializes a new instance of DashboardController.</summary>
    /// <param name="proxyState">YARP proxy state lookup.</param>
    /// <param name="dynamicConfig">Dynamic YARP config service.</param>
    /// <param name="env">Web host environment.</param>
    /// <param name="logStore">Proxy log store.</param>
    /// <param name="options">Dashboard options.</param>
    public DashboardController(
        IProxyStateLookup proxyState,
        DynamicYarpConfigService dynamicConfig,
        IWebHostEnvironment env,
        ProxyLogStore logStore,
        IOptions<DashboardOptions> options)
    {
        _proxyState = proxyState;
        _dynamicConfig = dynamicConfig;
        _env = env;
        _logStore = logStore;
        _options = options.Value;
    }

    /// <summary>Dashboard home page.</summary>
    [HttpGet("")]
    public IActionResult Index()
    {
        ViewBag.DashboardRoutePrefix = RoutePrefix;
        ViewBag.EnableProxyLogging = _options.EnableProxyLogging;
        return View();
    }

    /// <summary>Login page.</summary>
    [HttpGet("login")]
    public IActionResult Login()
    {
        ViewBag.DashboardRoutePrefix = RoutePrefix;
        ViewBag.AuthMode = _options.AuthMode;
        return View();
    }

    /// <summary>Login POST - validate credentials and return JWT.</summary>
    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return Json(new { code = 400, message = "Username and password are required" });

        bool valid = _options.AuthMode switch
        {
            DashboardAuthMode.CustomJwt =>
                request.Username == _options.JwtUsername && request.Password == _options.JwtPassword,
            DashboardAuthMode.DefaultJwt =>
                request.Username == "admin" && request.Password == _options.JwtPassword,
            _ => false
        };

        if (!valid)
            return Json(new { code = 401, message = "Invalid credentials" });

        var token = DashboardJwtHelper.GenerateToken(request.Username, _options.JwtSecret!);

        Response.Cookies.Append("dashboard_token", token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddHours(8)
        });

        return Json(new { code = 200, token });
    }

    /// <summary>Gateway basic info.</summary>
    [HttpGet("info")]
    public IActionResult GetInfo()
    {
        var process = Process.GetCurrentProcess();
        var uptime = DateTime.Now - _startTime;
        var memoryMb = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 1);

        return Json(new
        {
            code = 200,
            data = new
            {
                version = _fileVersion,
                environment = _env.EnvironmentName,
                startTime = _startTime.ToString("yyyy-MM-dd HH:mm:ss"),
                uptime = $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s",
                memoryMb,
                machineName = Environment.MachineName,
                processId = process.Id
            }
        });
    }

    /// <summary>Cluster status and config.</summary>
    [HttpGet("clusters")]
    public IActionResult GetClusters()
    {
        var clusters = _proxyState.GetClusters().Select(cluster =>
        {
            var config = cluster.Model?.Config;
            var destinations = cluster.Destinations.Select(d =>
            {
                var dc = d.Value.Model?.Config;
                return new
                {
                    name = d.Key,
                    address = dc?.Address,
                    health = dc?.Health,
                    host = dc?.Host,
                    metadata = dc?.Metadata?.Count > 0
                        ? dc.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value) : null,
                    activeHealth = d.Value.Health.Active.ToString(),
                    passiveHealth = d.Value.Health.Passive.ToString()
                };
            }).ToList();

            return new
            {
                clusterId = cluster.ClusterId,
                loadBalancingPolicy = config?.LoadBalancingPolicy ?? "Default",

                sessionAffinity = config?.SessionAffinity != null ? new
                {
                    enabled = config.SessionAffinity.Enabled,
                    policy = config.SessionAffinity.Policy?.ToString(),
                    failurePolicy = config.SessionAffinity.FailurePolicy?.ToString(),
                    affinityKeyName = config.SessionAffinity.AffinityKeyName,
                    cookie = config.SessionAffinity.Cookie != null ? new
                    {
                        domain = config.SessionAffinity.Cookie.Domain,
                        path = config.SessionAffinity.Cookie.Path,
                        expiration = config.SessionAffinity.Cookie.Expiration?.ToString(),
                        maxAge = config.SessionAffinity.Cookie.MaxAge?.ToString(),
                        securePolicy = config.SessionAffinity.Cookie.SecurePolicy?.ToString(),
                        httpOnly = config.SessionAffinity.Cookie.HttpOnly,
                        sameSite = config.SessionAffinity.Cookie.SameSite?.ToString(),
                        isEssential = config.SessionAffinity.Cookie.IsEssential
                    } : null
                } : null,

                healthCheck = config?.HealthCheck != null ? new
                {
                    active = config.HealthCheck.Active != null ? new
                    {
                        enabled = config.HealthCheck.Active.Enabled,
                        interval = config.HealthCheck.Active.Interval?.ToString(),
                        timeout = config.HealthCheck.Active.Timeout?.ToString(),
                        policy = config.HealthCheck.Active.Policy,
                        path = config.HealthCheck.Active.Path,
                        query = config.HealthCheck.Active.Query
                    } : null,
                    passive = config.HealthCheck.Passive != null ? new
                    {
                        enabled = config.HealthCheck.Passive.Enabled,
                        policy = config.HealthCheck.Passive.Policy,
                        reactivationPeriod = config.HealthCheck.Passive.ReactivationPeriod?.ToString()
                    } : null,
                    availableDestinationsPolicy = config.HealthCheck.AvailableDestinationsPolicy
                } : null,

                httpClient = config?.HttpClient != null ? new
                {
                    sslProtocols = config.HttpClient.SslProtocols,
                    dangerousAcceptAnyServerCertificate = config.HttpClient.DangerousAcceptAnyServerCertificate,
                    maxConnectionsPerServer = config.HttpClient.MaxConnectionsPerServer,
                    enableMultipleHttp2Connections = config.HttpClient.EnableMultipleHttp2Connections,
                    requestHeaderEncoding = config?.HttpClient?.RequestHeaderEncoding,
                    responseHeaderEncoding = config?.HttpClient?.ResponseHeaderEncoding,
                    webProxy = config!.HttpClient.WebProxy != null ? new
                    {
                        address = config!.HttpClient.WebProxy.Address?.ToString(),
                        bypassOnLocal = config!.HttpClient.WebProxy.BypassOnLocal,
                        useDefaultCredentials = config!.HttpClient.WebProxy.UseDefaultCredentials
                    } : null
                } : null,

                httpRequest = config?.HttpRequest != null ? new
                {
                    activityTimeout = config.HttpRequest.ActivityTimeout?.ToString(),
                    version = config.HttpRequest.Version?.ToString(),
                    versionPolicy = config.HttpRequest.VersionPolicy?.ToString(),
                    allowResponseBuffering = config.HttpRequest.AllowResponseBuffering
                } : null,

                metadata = config?.Metadata?.Count > 0
                    ? config.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value) : null,

                destinations,
                healthyCount = destinations.Count(d => d.activeHealth == "Healthy"),
                unknownCount = destinations.Count(d => d.activeHealth == "Unknown"),
                unhealthyCount = destinations.Count(d => d.activeHealth == "Unhealthy"),
                totalCount = destinations.Count
            };
        }).ToList();

        return Json(new { code = 200, data = clusters });
    }

    /// <summary>Route configuration.</summary>
    [HttpGet("routes")]
    public IActionResult GetRoutes()
    {
        var clusterDest = _proxyState.GetClusters()
            .ToDictionary(c => c.ClusterId, c => c.Destinations.Values.Select(d => (object)new
            {
                name = d.DestinationId,
                address = d.Model?.Config?.Address
            }).ToList(), StringComparer.OrdinalIgnoreCase);

        // Transforms from DynamicYarpConfigService (covers both file-based and dynamic routes)
        Dictionary<string, List<Dictionary<string, string>>?>? configTransforms = null;
        try
        {
            var dr = _dynamicConfig.GetRoutes();
            if (dr?.Count > 0)
            {
                configTransforms = new(StringComparer.OrdinalIgnoreCase);
                foreach (var r in dr)
                {
                    if (r.Transforms?.Count > 0)
                        configTransforms[r.RouteId] = r.Transforms.Select(t => new Dictionary<string, string>(t)).ToList();
                }
            }
        }
        catch (Exception)
        {
            // Log transform extraction failure but continue rendering routes
            // Log transform extraction failure but continue rendering routes
        }

        var routes = _proxyState.GetRoutes().Select(route =>
        {
            var rc = route.Config;
            var match = rc.Match;

            List<object>? destinations = null;
            if (rc.ClusterId != null && clusterDest.TryGetValue(rc.ClusterId, out var dests))
                destinations = dests;

            List<Dictionary<string, string>>? transforms = null;
            if (rc.Transforms?.Count > 0)
                transforms = rc.Transforms.Select(t => new Dictionary<string, string>(t)).ToList();
            else if (configTransforms != null && configTransforms.TryGetValue(rc.RouteId, out var ct) && ct != null)
                transforms = ct;

            return new
            {
                routeId = rc.RouteId,
                clusterId = rc.ClusterId,
                path = match.Path,
                methods = match.Methods,
                hosts = match.Hosts,
                order = rc.Order,
                authorizationPolicy = rc.AuthorizationPolicy,
                corsPolicy = rc.CorsPolicy,
                outputCachePolicy = rc.OutputCachePolicy,
                maxRequestBodySize = rc.MaxRequestBodySize,
                destinations,
                transforms,
                rateLimiterPolicy = rc.RateLimiterPolicy,
                timeoutPolicy = rc.TimeoutPolicy,
                timeout = rc.Timeout?.ToString(),
                metadata = rc.Metadata?.Count > 0 ? rc.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value) : null,
                headers = match.Headers?.Count > 0
                    ? match.Headers.Select(h => new { name = h.Name, values = h.Values, mode = h.Mode.ToString() }).ToList()
                    : null,
                queryParameters = match.QueryParameters?.Count > 0
                    ? match.QueryParameters.Select(q => new { name = q.Name, values = q.Values, mode = q.Mode.ToString() }).ToList()
                    : null
            };
        }).OrderBy(r => r.order).ToList();

        return Json(new { code = 200, data = routes });
    }

    /// <summary>Recent YARP proxy logs.</summary>
    [HttpGet("logs")]
    public IActionResult GetLogs([FromQuery] int count = 100)
    {
        if (!_options.EnableProxyLogging)
            return Json(new { code = 200, data = new { entries = new List<LogEntry>(), bufferSize = 0, evictedCount = 0L } });

        var snapshot = _logStore.GetRecent(count);
        return Json(new { code = 200, data = snapshot });
    }

    /// <summary>Clear all logs.</summary>
    [HttpDelete("logs")]
    public IActionResult ClearLogs()
    {
        if (!_options.EnableProxyLogging)
            return Json(new { code = 200 });

        _logStore.Clear();
        return Json(new { code = 200, message = "Logs cleared" });
    }
}
