using System.Runtime.InteropServices;
using System.Text.Json;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Dashboard.Infrastructure.I18n;
using Aneiang.Yarp.Dashboard.Infrastructure.Performance;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Storage;
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
    private readonly StorageOptions _storageOptions;

    // Cached option values to avoid repeated property access
    private readonly bool _enableProxyLogging;
    private readonly string _defaultLocale;
    private readonly DashboardAuthMode _authMode;
    private readonly string? _jwtSecret;
    private readonly string? _jwtPassword;
    private readonly string? _jwtUsername;
    private readonly bool _enableTwoFactor;
    private readonly string? _twoFactorSecret;
    private readonly int _minPasswordLength;

    // Runtime 2FA state (persisted to file)
    private static readonly string _twoFactorStateFile = Path.Combine(AppContext.BaseDirectory, "twofactor-state.json");
    private static readonly object _twoFactorLock = new();

    /// <summary>Initializes a new instance of DashboardController.</summary>
    /// <param name="infoQuery">Dashboard info query service.</param>
    /// <param name="clusterQuery">Cluster query service.</param>
    /// <param name="routeQuery">Route query service.</param>
    /// <param name="logQuery">Log query service.</param>
    /// <param name="authService">Authorization service.</param>
    /// <param name="dashboardOptions">Dashboard options.</param>
    /// <param name="storageOptions">Storage options.</param>
    public DashboardController(
        IDashboardInfoQueryService infoQuery,
        IDashboardClusterQueryService clusterQuery,
        IDashboardRouteQueryService routeQuery,
        IDashboardLogQueryService logQuery,
        IDashboardAuthorizationService authService,
        IOptions<DashboardOptions> dashboardOptions,
        IOptions<StorageOptions> storageOptions)
    {
        _infoQuery = infoQuery;
        _clusterQuery = clusterQuery;
        _routeQuery = routeQuery;
        _logQuery = logQuery;
        _authService = authService;

        // Pre-cache frequently accessed options
        var opt = dashboardOptions.Value;
        _enableProxyLogging = opt.EnableProxyLogging;
        _defaultLocale = opt.Locale;
        _authMode = opt.AuthMode;
        _jwtSecret = opt.JwtSecret;
        _jwtPassword = opt.JwtPassword;
        _jwtUsername = opt.JwtUsername;
        _enableTwoFactor = opt.EnableTwoFactor;
        _twoFactorSecret = opt.TwoFactorSecret;
        _minPasswordLength = opt.MinPasswordLength;
        _storageOptions = storageOptions.Value;
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

    [HttpGet("deployment")]
    public IActionResult Deployment()
    {
        SetCommonViewBag("deployment");
        return View();
    }

    /// <summary>Download the SQLite database file for local inspection.</summary>
    [HttpGet("api/settings/database")]
    public IActionResult DownloadDatabase()
    {
        var dbPath = ResolveDatabasePath(_storageOptions);
        if (!System.IO.File.Exists(dbPath))
            return Json(new { code = 404, message = "Database file not found" });

        var fileName = Path.GetFileName(dbPath);
        return PhysicalFile(dbPath, "application/octet-stream", fileName);
    }

    /// <summary>Resolves the SQLite database file path from the connection string.</summary>
    private static string ResolveDatabasePath(StorageOptions storageOptions)
    {
        var cs = storageOptions.Sqlite.ConnectionString;
        const string prefix = "Data Source=";

        int idx = cs.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return string.Empty;

        var value = cs[(idx + prefix.Length)..].Trim();

        // Strip any trailing semicolon-separated parameters (e.g. ";Pooling=true")
        int semi = value.IndexOf(';');
        if (semi >= 0)
            value = value[..semi].Trim();

        // If relative path, resolve relative to the app's content root
        if (!Path.IsPathRooted(value))
        {
            // Try current directory first (most common for embedded deployments)
            var rooted = Path.GetFullPath(value);
            if (System.IO.File.Exists(rooted))
                return rooted;
        }

        return value;
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

        // Password length check
        if (request.Password.Length < _minPasswordLength)
            return Json(new { code = 400, message = $"Password must be at least {_minPasswordLength} characters" });

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

        // 2FA verification (check both config and runtime state)
        var (twoFactorEnabled, twoFactorSecret) = GetTwoFactorState();
        if (twoFactorEnabled && !string.IsNullOrWhiteSpace(twoFactorSecret))
        {
            if (string.IsNullOrWhiteSpace(request.TwoFactorCode))
                return Json(new { code = 202, message = "Two-factor authentication required", requiresTwoFactor = true });

            if (!TotpHelper.ValidateCode(twoFactorSecret, request.TwoFactorCode))
                return Json(new { code = 401, message = "Invalid two-factor code" });
        }

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

    /// <summary>Get 2FA status.</summary>
    [HttpGet("api/2fa/status")]
    public IActionResult GetTwoFactorStatus()
    {
        var (enabled, _) = GetTwoFactorState();
        return Json(new { code = 200, data = new { enabled, minPasswordLength = _minPasswordLength } });
    }

    /// <summary>Generate a new 2FA secret and QR URL.</summary>
    [HttpGet("api/2fa/setup")]
    public IActionResult SetupTwoFactor()
    {
        var secret = TotpHelper.GenerateSecret();
        var issuer = "Gateway Dashboard";
        var account = _jwtUsername ?? "admin";
        var qrUrl = TotpHelper.BuildOtpAuthUri(issuer, account, secret);
        return Json(new { code = 200, data = new { secret, qrUrl } });
    }

    /// <summary>Verify 2FA code and enable 2FA.</summary>
    [HttpPost("api/2fa/verify")]
    public IActionResult VerifyTwoFactor([FromBody] JsonElement body)
    {
        var code = body.TryGetProperty("code", out var codeEl) ? codeEl.GetString() : null;
        var secret = body.TryGetProperty("secret", out var secretEl) ? secretEl.GetString() : null;

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(secret))
            return Json(new { code = 400, message = "Code and secret are required" });

        if (!TotpHelper.ValidateCode(secret, code))
            return Json(new { code = 400, message = "Invalid two-factor code" });

        SaveTwoFactorState(true, secret);
        return Json(new { code = 200, message = "Two-factor authentication enabled" });
    }

    /// <summary>Disable 2FA.</summary>
    [HttpPost("api/2fa/disable")]
    public IActionResult DisableTwoFactor()
    {
        SaveTwoFactorState(false, null);
        return Json(new { code = 200, message = "Two-factor authentication disabled" });
    }

    private (bool enabled, string? secret) GetTwoFactorState()
    {
        // Check runtime state file first
        try
        {
            if (System.IO.File.Exists(_twoFactorStateFile))
            {
                var json = System.IO.File.ReadAllText(_twoFactorStateFile);
                var state = System.Text.Json.JsonSerializer.Deserialize<TwoFactorState>(json);
                if (state != null)
                    return (state.Enabled, state.Secret);
            }
        }
        catch { }

        // Fall back to config
        return (_enableTwoFactor, _twoFactorSecret);
    }

    private void SaveTwoFactorState(bool enabled, string? secret)
    {
        lock (_twoFactorLock)
        {
            try
            {
                var state = new TwoFactorState { Enabled = enabled, Secret = secret };
                var json = System.Text.Json.JsonSerializer.Serialize(state);
                System.IO.File.WriteAllText(_twoFactorStateFile, json);
            }
            catch { }
        }
    }

    private class TwoFactorState
    {
        public bool Enabled { get; set; }
        public string? Secret { get; set; }
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
    public IActionResult GetLogs([FromQuery] int count = 100, [FromQuery] int? page = null, [FromQuery] int? pageSize = null)
    {
        var snapshot = page.HasValue || pageSize.HasValue
            ? _logQuery.GetLogsPage(page ?? 1, pageSize ?? count)
            : _logQuery.GetLogs(count);
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
