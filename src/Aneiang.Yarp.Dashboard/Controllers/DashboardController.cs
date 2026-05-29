using System.Reflection;
using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Dashboard.Models.Dtos;
using Aneiang.Yarp.Dashboard.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Controllers;

/// <summary>Gateway maintenance dashboard.</summary>
public class DashboardController : Controller
{
    /// <summary>Route prefix, set by convention at startup.</summary>
    internal static string RoutePrefix { get; set; } = "apigateway";

    private readonly IDashboardInfoQueryService _infoQuery;
    private readonly IDashboardClusterQueryService _clusterQuery;
    private readonly IDashboardRouteQueryService _routeQuery;
    private readonly IDashboardLogQueryService _logQuery;
    private readonly IDashboardAuthorizationService _authService;
    private readonly DashboardOptions _options;

    /// <summary>Initializes a new instance of DashboardController.</summary>
    /// <param name="infoQuery">Dashboard info query service.</param>
    /// <param name="clusterQuery">Cluster query service.</param>
    /// <param name="routeQuery">Route query service.</param>
    /// <param name="logQuery">Log query service.</param>
    /// <param name="authService">Authorization service.</param>
    /// <param name="options">Dashboard options.</param>
    public DashboardController(
        IDashboardInfoQueryService infoQuery,
        IDashboardClusterQueryService clusterQuery,
        IDashboardRouteQueryService routeQuery,
        IDashboardLogQueryService logQuery,
        IDashboardAuthorizationService authService,
        IOptions<DashboardOptions> options)
    {
        _infoQuery = infoQuery;
        _clusterQuery = clusterQuery;
        _routeQuery = routeQuery;
        _logQuery = logQuery;
        _authService = authService;
        _options = options.Value;
    }

    private void SetCommonViewBag(string? currentPage = null)
    {
        ViewBag.DashboardRoutePrefix = RoutePrefix;
        ViewBag.EnableProxyLogging = _options.EnableProxyLogging;
        ViewBag.EnableMetrics = _options.EnableMetrics;
        ViewBag.EnableResponseCache = _options.EnableResponseCache;
        ViewBag.Locale = ResolveLocale();
        ViewBag.AllI18nJson = DashboardI18n.AllAsJson(ViewBag.Locale);
        ViewBag.CurrentPage = currentPage ?? "overview";
    }

    /// <summary>Dashboard overview page.</summary>
    [HttpGet("")]
    public IActionResult Overview()
    {
        SetCommonViewBag("overview");
        return View();
    }

    /// <summary>Dashboard clusters page.</summary>
    [HttpGet("clusters")]
    public IActionResult Clusters()
    {
        SetCommonViewBag("clusters");
        return View();
    }

    /// <summary>Dashboard routes page.</summary>
    [HttpGet("routes")]
    public IActionResult Routes()
    {
        SetCommonViewBag("routes");
        return View();
    }

    /// <summary>Dashboard logs page.</summary>
    [HttpGet("logs")]
    public IActionResult Logs()
    {
        SetCommonViewBag("logs");
        return View();
    }

    /// <summary>Dashboard statistics page.</summary>
    [HttpGet("stats")]
    public IActionResult Stats()
    {
        SetCommonViewBag("stats");
        return View();
    }

    /// <summary>Dashboard configuration history page.</summary>
    [HttpGet("history")]
    public IActionResult History()
    {
        SetCommonViewBag("history");
        return View();
    }

    /// <summary>Dashboard audit log page.</summary>
    [HttpGet("audit")]
    public IActionResult Audit()
    {
        SetCommonViewBag("audit");
        return View();
    }

    /// <summary>Dashboard settings page (webhook, import/export, etc.).</summary>
    [HttpGet("settings")]
    public IActionResult Settings()
    {
        SetCommonViewBag("settings");
        return View();
    }

    /// <summary>Dashboard health check page.</summary>
    [HttpGet("healthcheck")]
    public IActionResult HealthCheck()
    {
        SetCommonViewBag("healthcheck");
        return View();
    }

    /// <summary>Dashboard metrics page.</summary>
    [HttpGet("metrics")]
    public IActionResult Metrics()
    {
        SetCommonViewBag("metrics");
        return View();
    }

    /// <summary>Dashboard response cache page.</summary>
    [HttpGet("responsecache")]
    public IActionResult ResponseCache()
    {
        SetCommonViewBag("responsecache");
        return View();
    }

    /// <summary>Dashboard login page.</summary>
    [HttpGet("login")]
    public IActionResult Login()
    {
        ViewBag.DashboardRoutePrefix = RoutePrefix;
        ViewBag.AuthMode = _options.AuthMode;
        ViewBag.Locale = ResolveLocale();
        ViewBag.AllI18nJson = DashboardI18n.AllAsJson(ViewBag.Locale);
        return View();
    }

    private string ResolveLocale()
    {
        // Check cookie first (set by client-side switch), then config default
        var cookieLocale = Request.Cookies["dashboard_locale"];
        if (!string.IsNullOrEmpty(cookieLocale))
            return cookieLocale == "en-US" ? "en-US" : "zh-CN";
        return _options.Locale == "en-US" ? "en-US" : "zh-CN";
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
    [HttpGet("api/info")]
    public IActionResult GetInfo()
    {
        var info = _infoQuery.GetInfo();
        return Json(new { code = 200, data = info });
    }

    /// <summary>Cluster status and config.</summary>
    [HttpGet("api/clusters")]
    public IActionResult GetClusters()
    {
        var clusters = _clusterQuery.GetClusters();
        return Json(new { code = 200, data = clusters });
    }

    /// <summary>Route configuration.</summary>
    [HttpGet("api/routes")]
    public IActionResult GetRoutes()
    {
        var routes = _routeQuery.GetRoutes();
        return Json(new { code = 200, data = routes });
    }

    /// <summary>Recent YARP proxy logs.</summary>
    [HttpGet("api/logs")]
    public IActionResult GetLogs([FromQuery] int count = 100)
    {
        var snapshot = _logQuery.GetLogs(count);
        return Json(new { code = 200, data = snapshot });
    }

    /// <summary>Clear all logs.</summary>
    [HttpDelete("api/logs")]
    public IActionResult ClearLogs()
    {
        _logQuery.ClearLogs();
        return Json(new { code = 200, message = "Logs cleared" });
    }

    /// <summary>Access statistics computed from the log buffer.</summary>
    [HttpGet("api/stats")]
    public IActionResult GetStats()
    {
        var snapshot = _logQuery.GetLogs(2000);
        var responses = snapshot.Entries.Where(e => e.EventType == LogEventType.ProxyResponse).ToList();
        var requests = snapshot.Entries.Where(e => e.EventType == LogEventType.ProxyRequest).ToList();

        if (responses.Count == 0)
            return Json(new { code = 200, data = new { hasData = false } });

        var totalRequests = responses.Count;
        var successCount = responses.Count(r => r.StatusCode >= 200 && r.StatusCode < 400);
        var errorCount = responses.Count(r => r.StatusCode >= 400);
        var latencies = responses.Where(r => r.ElapsedMs.HasValue).Select(r => r.ElapsedMs!.Value).OrderBy(l => l).ToList();

        var avgLatency = latencies.Count > 0 ? latencies.Average() : 0;
        var p50 = Percentile(latencies, 0.50);
        var p90 = Percentile(latencies, 0.90);
        var p99 = Percentile(latencies, 0.99);

        // Status code distribution
        var statusCodes = responses
            .GroupBy(r => r.StatusCode ?? 0)
            .Select(g => new { code = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count)
            .ToList();

        // Top routes by request count (from requests, matched by traceId)
        var routeCounts = requests
            .GroupBy(r => r.RouteId ?? "unknown")
            .Select(g => new { route = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count)
            .Take(10)
            .ToList();

        // Top clusters
        var clusterCounts = requests
            .GroupBy(r => r.ClusterId ?? "unknown")
            .Select(g => new { cluster = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count)
            .Take(10)
            .ToList();

        // Requests per minute (based on response timestamps)
        var now = responses.Max(r => r.Timestamp);
        var oneMinAgo = now.AddMinutes(-1);
        var recentCount = responses.Count(r => r.Timestamp >= oneMinAgo);
        var requestsPerMin = recentCount; // approximate

        return Json(new { code = 200, data = new
        {
            hasData = true,
            totalRequests,
            successCount,
            errorCount,
            successRate = totalRequests > 0 ? Math.Round((double)successCount / totalRequests * 100, 1) : 0,
            errorRate = totalRequests > 0 ? Math.Round((double)errorCount / totalRequests * 100, 1) : 0,
            avgLatency = Math.Round(avgLatency, 1),
            p50 = Math.Round(p50, 1),
            p90 = Math.Round(p90, 1),
            p99 = Math.Round(p99, 1),
            requestsPerMin,
            statusCodes,
            topRoutes = routeCounts,
            topClusters = clusterCounts,
            computedAt = DateTime.UtcNow
        }});
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        if (sorted.Count == 1) return sorted[0];
        var idx = p * (sorted.Count - 1);
        var lower = (int)Math.Floor(idx);
        var upper = (int)Math.Ceiling(idx);
        if (lower == upper) return sorted[lower];
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (idx - lower);
    }

    /// <summary>Rate limiting configuration status.</summary>
    [HttpGet("api/rate-limit")]
    public IActionResult GetRateLimitStatus()
    {
        return Json(new { code = 200, data = new
        {
            enabled = _options.EnableRateLimiting,
            permitLimit = _options.RateLimitPermitLimit,
            window = _options.RateLimitWindow,
            queueLimit = _options.RateLimitQueueLimit
        }});
    }

    /// <summary>Get current authorization status and mode.</summary>
    [HttpGet("api/auth/status")]
    public IActionResult GetAuthStatus()
    {
        var authModeDescription = _authService.GetAuthModeDescription();
        var isAuthEnabled = _options.AuthMode != DashboardAuthMode.None || _options.AuthorizeRequest != null;

        return Json(new
        {
            code = 200,
            data = new
            {
                isAuthEnabled,
                authMode = _options.AuthMode.ToString(),
                authModeDescription,
                locale = _options.Locale
            }
        });
    }

    /// <summary>Logout - clear the auth token cookie.</summary>
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("dashboard_token");
        return Json(new { code = 200, message = "Logged out successfully" });
    }
}
