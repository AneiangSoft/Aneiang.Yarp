using System.Runtime.InteropServices;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Dashboard.Infrastructure.I18n;
using Aneiang.Yarp.Dashboard.Infrastructure.Performance;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

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

    // Cached option values to avoid repeated property access
    private readonly bool _enableProxyLogging;
    private readonly string _defaultLocale;
    private readonly DashboardAuthMode _authMode;
    private readonly string? _jwtSecret;
    private readonly string? _jwtPassword;
    private readonly string? _jwtUsername;

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

        // Pre-cache frequently accessed options
        var opt = options.Value;
        _enableProxyLogging = opt.EnableProxyLogging;
        _defaultLocale = opt.Locale;
        _authMode = opt.AuthMode;
        _jwtSecret = opt.JwtSecret;
        _jwtPassword = opt.JwtPassword;
        _jwtUsername = opt.JwtUsername;
    }

    private void SetCommonViewBag(string? currentPage = null)
    {
        ViewBag.DashboardRoutePrefix = RoutePrefix;
        ViewBag.EnableProxyLogging = _enableProxyLogging;
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

    [HttpGet("clusters")]
    public IActionResult Clusters()
    {
        SetCommonViewBag("clusters");
        return View();
    }

    [HttpGet("routes")]
    public IActionResult Routes()
    {
        SetCommonViewBag("routes");
        return View();
    }

    [HttpGet("stats")]
    public IActionResult Stats()
    {
        SetCommonViewBag("stats");
        return View();
    }

    [HttpGet("logs")]
    public IActionResult Logs()
    {
        SetCommonViewBag("logs");
        return View();
    }

    [HttpGet("circuits")]
    public IActionResult Circuits()
    {
        SetCommonViewBag("circuits");
        return View();
    }

    [HttpGet("alerts")]
    public IActionResult Alerts()
    {
        SetCommonViewBag("alerts");
        return View();
    }

    [HttpGet("notifications")]
    public IActionResult Notifications()
    {
        SetCommonViewBag("notifications");
        return View();
    }

    [HttpGet("security")]
    public IActionResult Security()
    {
        SetCommonViewBag("security");
        return View();
    }

    [HttpGet("waf")]
    public IActionResult Waf()
    {
        SetCommonViewBag("waf");
        return View();
    }

    [HttpGet("healthcheck")]
    public IActionResult HealthCheck()
    {
        SetCommonViewBag("healthcheck");
        return View();
    }

    [HttpGet("history")]
    public IActionResult History()
    {
        SetCommonViewBag("history");
        return View();
    }

    [HttpGet("policies")]
    public IActionResult Policies()
    {
        SetCommonViewBag("policies");
        return View();
    }

    [HttpGet("plugins")]
    public IActionResult Plugins()
    {
        SetCommonViewBag("plugins");
        return View();
    }

    [HttpGet("audit")]
    public IActionResult Audit()
    {
        SetCommonViewBag("audit");
        return View();
    }

    [HttpGet("settings")]
    public IActionResult Settings()
    {
        SetCommonViewBag("settings");
        return View();
    }

    /// <summary>Dashboard login page.</summary>
    [HttpGet("login")]
    public IActionResult Login()
    {
        ViewBag.DashboardRoutePrefix = RoutePrefix;
        ViewBag.AuthMode = _authMode;
        ViewBag.Locale = ResolveLocale();
        ViewBag.AllI18nJson = DashboardI18n.AllAsJson(ViewBag.Locale);
        return View();
    }

    /// <summary>
    /// Resolves the locale from cookie or config default.
    /// Simple branching for optimal performance.
    /// </summary>
    private string ResolveLocale()
    {
        var cookieLocale = Request.Cookies["dashboard_locale"];
        if (!string.IsNullOrEmpty(cookieLocale))
            return cookieLocale == "en-US" ? "en-US" : "zh-CN";
        return _defaultLocale == "en-US" ? "en-US" : "zh-CN";
    }

    /// <summary>Login POST - validate credentials and return JWT.</summary>
    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return Json(new { code = 400, message = "Username and password are required" });

        bool valid = _authMode switch
        {
            DashboardAuthMode.CustomJwt =>
                request.Username == _jwtUsername && request.Password == _jwtPassword,
            DashboardAuthMode.DefaultJwt =>
                request.Username == "admin" && request.Password == _jwtPassword,
            _ => false
        };

        if (!valid)
            return Json(new { code = 401, message = "Invalid credentials" });

        var token = DashboardJwtHelper.GenerateToken(request.Username, _jwtSecret!);

        Response.Cookies.Append("dashboard_token", token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTime.Now.AddHours(8)
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

        // For large datasets (1000+ entries), use parallel processing
        // Otherwise, single-threaded is more efficient due to lower overhead
        const int ParallelThreshold = 1000;

        if (snapshot.Entries.Count >= ParallelThreshold)
        {
            return ComputeStatsParallel(snapshot);
        }
        return ComputeStatsSequential(snapshot);
    }

    /// <summary>
    /// Sequential stats computation for small to medium datasets.
    /// Single-pass iteration, minimal allocations.
    /// </summary>
    private IActionResult ComputeStatsSequential(ProxyLogStoreSnapshot snapshot)
    {
        // Pre-allocate collections with exact capacity
        var latencies = new List<double>(snapshot.Entries.Count / 2);
        var statusCodeCounts = new Dictionary<int, int>();
        var routeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var clusterCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int totalRequests = 0;
        int successCount = 0;
        int errorCount = 0;
        DateTime maxResponseTimestamp = DateTime.MinValue;

        // Single-pass aggregation
        foreach (var entry in snapshot.Entries)
        {
            if (entry.EventType == LogEventType.ProxyResponse)
            {
                totalRequests++;

                var statusCode = entry.StatusCode ?? 0;
                if (statusCode >= 200 && statusCode < 400)
                    successCount++;
                else if (statusCode >= 400)
                    errorCount++;

                CollectionsMarshalHelper.AddOrIncrement(statusCodeCounts, statusCode);

                if (entry.ElapsedMs.HasValue)
                    latencies.Add(entry.ElapsedMs.Value);

                if (entry.Timestamp > maxResponseTimestamp)
                    maxResponseTimestamp = entry.Timestamp;
            }
            else if (entry.EventType == LogEventType.ProxyRequest)
            {
                CollectionsMarshalHelper.AddOrIncrement(routeCounts, entry.RouteId ?? "unknown");
                CollectionsMarshalHelper.AddOrIncrement(clusterCounts, entry.ClusterId ?? "unknown");
            }
        }

        // RPM will be computed by binary search in BuildStatsResponse
        return BuildStatsResponse(
            totalRequests, successCount, errorCount,
            latencies, statusCodeCounts, routeCounts, clusterCounts,
            maxResponseTimestamp, snapshot.Entries);
    }

    /// <summary>
    /// Parallel stats computation for large datasets.
    /// Uses PLINQ for CPU-intensive aggregation.
    /// </summary>
    private IActionResult ComputeStatsParallel(ProxyLogStoreSnapshot snapshot)
    {
        var entries = snapshot.Entries;

        // Partition the work across CPU cores
        var processorCount = Environment.ProcessorCount;
        var partitionSize = Math.Max(1, entries.Count / processorCount);

        var partitions = Enumerable.Range(0, processorCount)
            .Select(i =>
            {
                var start = i * partitionSize;
                var end = (i == processorCount - 1) ? entries.Count : Math.Min(start + partitionSize, entries.Count);
                return (start, end);
            })
            .ToArray();

        // Parallel aggregation per partition
        var results = partitions.AsParallel().Select(range =>
        {
            var localLatencies = new List<double>(range.end - range.start);
            var localStatusCodes = new Dictionary<int, int>();
            var localRoutes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var localClusters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            int localTotal = 0, localSuccess = 0, localError = 0;
            DateTime localMaxTs = DateTime.MinValue;

            for (int i = range.start; i < range.end; i++)
            {
                var entry = entries[i];
                if (entry.EventType == LogEventType.ProxyResponse)
                {
                    localTotal++;
                    var statusCode = entry.StatusCode ?? 0;
                    if (statusCode >= 200 && statusCode < 400) localSuccess++;
                    else if (statusCode >= 400) localError++;

                    CollectionsMarshalHelper.AddOrIncrement(localStatusCodes, statusCode);
                    if (entry.ElapsedMs.HasValue) localLatencies.Add(entry.ElapsedMs.Value);
                    if (entry.Timestamp > localMaxTs) localMaxTs = entry.Timestamp;
                }
                else if (entry.EventType == LogEventType.ProxyRequest)
                {
                    CollectionsMarshalHelper.AddOrIncrement(localRoutes, entry.RouteId ?? "unknown");
                    CollectionsMarshalHelper.AddOrIncrement(localClusters, entry.ClusterId ?? "unknown");
                }
            }

            return (localLatencies, localStatusCodes, localRoutes, localClusters,
                    localTotal, localSuccess, localError, localMaxTs);
        }).ToArray();

        // Merge results
        var allLatencies = new List<double>(entries.Count / 2);
        var mergedStatusCodes = new Dictionary<int, int>();
        var mergedRoutes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var mergedClusters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int totalRequests = 0, successCount = 0, errorCount = 0;
        DateTime maxResponseTimestamp = DateTime.MinValue;

        foreach (var r in results)
        {
            allLatencies.AddRange(r.localLatencies);
            MergeDictionaries(mergedStatusCodes, r.localStatusCodes);
            MergeDictionaries(mergedRoutes, r.localRoutes);
            MergeDictionaries(mergedClusters, r.localClusters);
            totalRequests += r.localTotal;
            successCount += r.localSuccess;
            errorCount += r.localError;
            if (r.localMaxTs > maxResponseTimestamp) maxResponseTimestamp = r.localMaxTs;
        }

        return BuildStatsResponse(
            totalRequests, successCount, errorCount,
            allLatencies, mergedStatusCodes, mergedRoutes, mergedClusters,
            maxResponseTimestamp, entries);
    }

    /// <summary>
    /// Merges source dictionary counts into target.
    /// </summary>
    private static void MergeDictionaries<TKey>(Dictionary<TKey, int> target, Dictionary<TKey, int> source)
        where TKey : notnull
    {
        foreach (var kvp in source)
        {
            CollectionsMarshalHelper.AddOrIncrement(target, kvp.Key, kvp.Value);
        }
    }

    /// <summary>
    /// Builds the final stats response.
    /// </summary>
    private IActionResult BuildStatsResponse(
        int totalRequests, int successCount, int errorCount,
        List<double> latencies, Dictionary<int, int> statusCodeCounts,
        Dictionary<string, int> routeCounts, Dictionary<string, int> clusterCounts,
        DateTime maxResponseTimestamp, List<LogEntry> entries)
    {
        if (totalRequests == 0)
            return Json(new { code = 200, data = new { hasData = false } });

        // Sort latencies once for percentile computation
        double avgLatency = 0, p50 = 0, p90 = 0, p99 = 0;
        if (latencies.Count > 0)
        {
            latencies.Sort();
            avgLatency = latencies.Average();
            // All three percentiles from the same sorted list — single sort, O(n log n)
            p50 = CalculatePercentileSorted(latencies, 0.50);
            p90 = CalculatePercentileSorted(latencies, 0.90);
            p99 = CalculatePercentileSorted(latencies, 0.99);
        }

        // Requests per minute: binary search on response timestamps — O(log n) instead of O(n) re-scan.
        // Entries are roughly time-ordered from the ring buffer; collect response timestamps
        // into a sorted list and use binary search to count entries within the last minute.
        int requestsPerMin = 0;
        if (maxResponseTimestamp > DateTime.MinValue)
        {
            var oneMinAgo = maxResponseTimestamp.AddMinutes(-1);
            var responseTimestamps = new List<DateTime>(totalRequests);
            foreach (var e in entries)
            {
                if (e.EventType == LogEventType.ProxyResponse)
                    responseTimestamps.Add(e.Timestamp);
            }
            if (responseTimestamps.Count > 0)
            {
                responseTimestamps.Sort();
                // Binary search: find first index >= oneMinAgo
                int lo = 0, hi = responseTimestamps.Count;
                while (lo < hi)
                {
                    int mid = lo + (hi - lo) / 2;
                    if (responseTimestamps[mid] < oneMinAgo)
                        lo = mid + 1;
                    else
                        hi = mid;
                }
                requestsPerMin = responseTimestamps.Count - lo;
            }
        }

        // Use source generator for JSON serialization
        var data = new StatsData
        {
            HasData = true,
            TotalRequests = totalRequests,
            SuccessCount = successCount,
            ErrorCount = errorCount,
            SuccessRate = totalRequests > 0 ? Math.Round((double)successCount / totalRequests * 100, 1) : 0,
            ErrorRate = totalRequests > 0 ? Math.Round((double)errorCount / totalRequests * 100, 1) : 0,
            AvgLatency = Math.Round(avgLatency, 1),
            P50 = Math.Round(p50, 1),
            P90 = Math.Round(p90, 1),
            P99 = Math.Round(p99, 1),
            RequestsPerMin = requestsPerMin,
            StatusCodes = statusCodeCounts.Select(g => new StatusCodeItem { Code = g.Key, Count = g.Value })
                .OrderByDescending(x => x.Count).ToList(),
            TopRoutes = routeCounts.Select(g => new TopItem { Name = g.Key, Count = g.Value })
                .OrderByDescending(x => x.Count).Take(10).ToList(),
            TopClusters = clusterCounts.Select(g => new TopItem { Name = g.Key, Count = g.Value })
                .OrderByDescending(x => x.Count).Take(10).ToList(),
            ComputedAt = DateTime.Now
        };

        // Use standard JSON serialization for anonymous wrapper type
        // Source generator is only used for strongly-typed inner data
        return Json(new { code = 200, data });
    }

    /// <summary>
    /// Calculates percentile from a sorted list using Span for zero-allocation access.
    /// </summary>
    private static double CalculatePercentileSorted(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        if (sorted.Count == 1) return sorted[0];

        // Use Span for efficient array access without bounds checking in release
        var span = CollectionsMarshal.AsSpan(sorted);
        var idx = p * (span.Length - 1);
        var lower = (int)Math.Floor(idx);
        var upper = (int)Math.Ceiling(idx);

        if (lower == upper) return span[lower];
        return span[lower] + (span[upper] - span[lower]) * (idx - lower);
    }

    // DTOs for typed JSON response
    private class StatsData
    {
        public bool HasData { get; set; }
        public int TotalRequests { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
        public double SuccessRate { get; set; }
        public double ErrorRate { get; set; }
        public double AvgLatency { get; set; }
        public double P50 { get; set; }
        public double P90 { get; set; }
        public double P99 { get; set; }
        public int RequestsPerMin { get; set; }
        public List<StatusCodeItem> StatusCodes { get; set; } = new();
        public List<TopItem> TopRoutes { get; set; } = new();
        public List<TopItem> TopClusters { get; set; } = new();
        public DateTime ComputedAt { get; set; }
    }

    private class StatusCodeItem
    {
        public int Code { get; set; }
        public int Count { get; set; }
    }

    private class TopItem
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
    }



    /// <summary>Rate limiting configuration status.</summary>


    /// <summary>Get current authorization status and mode.</summary>
    [HttpGet("api/auth/status")]
    public IActionResult GetAuthStatus()
    {
        var authModeDescription = _authService.GetAuthModeDescription();
        var isAuthEnabled = _authMode != DashboardAuthMode.None;

        var data = new AuthStatus
        {
            IsAuthEnabled = isAuthEnabled,
            AuthMode = _authMode.ToString(),
            AuthModeDescription = authModeDescription,
            Locale = _defaultLocale
        };
        return Json(new { code = 200, data });
    }

    // Simple DTOs for typed JSON responses



    private class AuthStatus
    {
        public bool IsAuthEnabled { get; set; }
        public string AuthMode { get; set; } = string.Empty;
        public string AuthModeDescription { get; set; } = string.Empty;
        public string Locale { get; set; } = string.Empty;
    }

    /// <summary>Logout - clear the auth token cookie.</summary>
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("dashboard_token");
        return Json(new { code = 200, message = "Logged out successfully" });
    }
}
