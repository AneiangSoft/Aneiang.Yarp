using System.Runtime.InteropServices;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Infrastructure.Performance;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>
/// Dashboard data API endpoints — info, clusters, routes, logs, and stats.
/// </summary>
public class DashboardApiController : Controller
{
    private readonly IDashboardInfoQueryService _infoQuery;
    private readonly IDashboardClusterQueryService _clusterQuery;
    private readonly IDashboardRouteQueryService _routeQuery;
    private readonly IDashboardLogQueryService _logQuery;
    private readonly IDashboardAuthorizationService _authService;
    private readonly IMemoryCache _memoryCache;
    private readonly IProxyLogRepository _logRepository;
    private readonly IProxyLogPersistenceService _persistenceService;
    private readonly LockFreeStatistics _statistics;
    private readonly LogSettingsService _logSettings;
    private readonly DashboardOptions _options;
    private readonly string _defaultLocale;
    private readonly DashboardAuthMode _authMode;

    /// <summary>
    /// Initializes a new instance of the <see cref="DashboardApiController"/> class.
    /// </summary>
    public DashboardApiController(
        IDashboardInfoQueryService infoQuery,
        IDashboardClusterQueryService clusterQuery,
        IDashboardRouteQueryService routeQuery,
        IDashboardLogQueryService logQuery,
        IDashboardAuthorizationService authService,
        IMemoryCache memoryCache,
        IProxyLogRepository logRepository,
        IProxyLogPersistenceService persistenceService,
        LockFreeStatistics statistics,
        LogSettingsService logSettings,
        IOptions<DashboardOptions> dashboardOptions)
    {
        _infoQuery = infoQuery;
        _clusterQuery = clusterQuery;
        _routeQuery = routeQuery;
        _logQuery = logQuery;
        _authService = authService;
        _memoryCache = memoryCache;
        _logRepository = logRepository;
        _persistenceService = persistenceService;
        _statistics = statistics;
        _logSettings = logSettings;
        _options = dashboardOptions.Value;
        _defaultLocale = _options.Locale;
        _authMode = _options.AuthMode;
    }

    #region Info
    /// <summary>Gateway basic info.</summary>
    [HttpGet("api/info")]
    public IActionResult GetInfo()
    {
        var info = _infoQuery.GetInfo();
        return Json(new { code = 200, data = info });
    }

    #endregion

    #region Clusters / Routes

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

    // ── Logs ──

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

    /// <summary>Historical log metadata from SQLite (paginated, filtered).</summary>
    [HttpGet("api/logs/history")]
    public async Task<IActionResult> GetLogHistory([FromQuery] ProxyLogSearchRequest request, CancellationToken ct)
    {
        if (!_options.LogPersistenceEnabled)
            return Json(new { code = 200, data = new ProxyLogSearchResult { Items = new List<ProxyLogMetaItem>(), TotalCount = 0 } });

        request.PageSize = Math.Clamp(request.PageSize, 1, 200);
        request.Page = Math.Max(1, request.Page);
        var result = await _logQuery.GetHistoryLogsAsync(request, ct);
        return Json(new { code = 200, data = result });
    }

    /// <summary>Single log detail (full body/headers) from SQLite.</summary>
    [HttpGet("api/logs/detail/{id}")]
    public async Task<IActionResult> GetLogDetail(long id, CancellationToken ct)
    {
        if (!_options.LogPersistenceEnabled)
            return Json(new { code = 404, message = "Log persistence is not enabled" });

        var detail = await _logQuery.GetLogDetailAsync(id, ct);
        if (detail == null)
            return Json(new { code = 404, message = "Log entry not found" });

        return Json(new { code = 200, data = detail });
    }

    /// <summary>Log persistence stats (dropped/written counts).</summary>
    [HttpGet("api/logs/stats")]
    public IActionResult GetLogStats()
    {
        return Json(new
        {
            code = 200,
            data = new
            {
                droppedCount = _persistenceService.DroppedCount,
                writtenCount = _persistenceService.WrittenCount,
                persistenceEnabled = _options.LogPersistenceEnabled,
                bufferCapacity = _options.LogBufferCapacity
            }
        });
    }

    /// <summary>Get current log settings (SQLite overrides → IOptionsMonitor → defaults).</summary>
    [HttpGet("api/logs/settings")]
    public async Task<IActionResult> GetLogSettings(CancellationToken ct)
    {
        var settings = await _logSettings.LoadAsync(ct);
        return Json(new { code = 200, data = settings });
    }

    /// <summary>Update log settings. Only provided fields are updated.</summary>
    [HttpPut("api/logs/settings")]
    public async Task<IActionResult> UpdateLogSettings([FromBody] LogSettingsUpdateRequest request, CancellationToken ct)
    {
        if (request == null)
            return Json(new { code = 400, message = "Request body is required" });

        // Validate: LogSamplingRate range
        if (request.LogSamplingRate.HasValue && (request.LogSamplingRate.Value < 0 || request.LogSamplingRate.Value > 1))
            return Json(new { code = 400, message = "LogSamplingRate must be between 0.0 and 1.0" });

        // Validate: LogMetaRetentionDays range
        if (request.LogMetaRetentionDays.HasValue && (request.LogMetaRetentionDays.Value < 1 || request.LogMetaRetentionDays.Value > 365))
            return Json(new { code = 400, message = "LogMetaRetentionDays must be between 1 and 365" });

        // Validate: MinLogLevel valid values
        if (request.MinLogLevel != null)
        {
            var validLevels = new[] { "Debug", "Information", "Warning", "Error", "Critical" };
            if (!validLevels.Contains(request.MinLogLevel, StringComparer.OrdinalIgnoreCase))
                return Json(new { code = 400, message = $"MinLogLevel must be one of: {string.Join(", ", validLevels)}" });
        }

        var updated = await _logSettings.SaveAsync(request, ct);
        return Json(new { code = 200, data = updated });
    }

    /// <summary>Reset log settings to defaults (clears SQLite overrides).</summary>
    [HttpPut("api/logs/settings/reset")]
    public async Task<IActionResult> ResetLogSettings(CancellationToken ct)
    {
        var defaults = await _logSettings.ResetAsync(ct);
        return Json(new { code = 200, data = defaults });
    }

    // ── Stats ──

    /// <summary>Access statistics. Cached for 10 seconds.
    /// Priority: 1) LockFreeStatistics snapshot (zero-allocation) → 2) SQL aggregation → 3) in-memory fallback.</summary>
    [HttpGet("api/stats")]
    public IActionResult GetStats()
    {
        // Priority 1: Lock-free statistics snapshot (zero-allocation, always available)
        var snapshot = _statistics.GetSnapshot();
        if (snapshot.TotalRequests > 0)
        {
            var cached = _memoryCache.GetOrCreate("dashboard:stats:lfs", entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10);

                var avgLatencyMs = snapshot.AvgLatencyMicros / 1000.0;
                var recentThreshold = DateTime.UtcNow.AddMinutes(-1);
                var requestsPerMin = snapshot.ComputedAt >= recentThreshold ? (int)(snapshot.TotalRequests / Math.Max(1, (DateTime.UtcNow - snapshot.ComputedAt).TotalMinutes)) : 0;

                return Json(new
                {
                    code = 200,
                    data = new StatsData
                    {
                        HasData = true,
                        TotalRequests = (int)snapshot.TotalRequests,
                        SuccessCount = (int)snapshot.SuccessCount,
                        ErrorCount = (int)snapshot.ErrorCount,
                        SuccessRate = snapshot.SuccessRate,
                        ErrorRate = snapshot.ErrorRate,
                        AvgLatency = Math.Round(avgLatencyMs, 1),
                        P50 = Math.Round(avgLatencyMs * 0.85, 1),  // Approximation from avg
                        P90 = Math.Round(avgLatencyMs * 1.5, 1),    // Approximation
                        P99 = Math.Round(avgLatencyMs * 2.5, 1),    // Approximation
                        RequestsPerMin = requestsPerMin,
                        StatusCodes = snapshot.StatusCodes.Select(g => new StatusCodeItem { Code = g.Key, Count = (int)g.Value })
                            .OrderByDescending(x => x.Count).ToList(),
                        TopRoutes = snapshot.TopRoutes.Select(g => new TopItem { Name = $"route:{g.Key}", Count = (int)g.Value })
                            .OrderByDescending(x => x.Count).Take(10).ToList(),
                        TopClusters = snapshot.TopClusters.Select(g => new TopItem { Name = $"cluster:{g.Key}", Count = (int)g.Value })
                            .OrderByDescending(x => x.Count).Take(10).ToList(),
                        ComputedAt = snapshot.ComputedAt
                    }
                }) as IActionResult;
            });

            if (cached != null) return cached;
        }

        // Priority 2: When persistence is enabled, prefer SQL aggregation (percentile-accurate)
        if (_options.LogPersistenceEnabled)
        {
            var sqlStats = _memoryCache.GetOrCreate("dashboard:stats", entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10);
                try
                {
                    var result = _logRepository.GetStatsAsync(5).GetAwaiter().GetResult();
                    if (result.TotalRequests == 0)
                        return Json(new { code = 200, data = new { hasData = false } }) as IActionResult;

                    return Json(new
                    {
                        code = 200,
                        data = new StatsData
                        {
                            HasData = true,
                            TotalRequests = (int)result.TotalRequests,
                            SuccessCount = (int)result.SuccessCount,
                            ErrorCount = (int)result.ErrorCount,
                            SuccessRate = result.TotalRequests > 0 ? Math.Round((double)result.SuccessCount / result.TotalRequests * 100, 1) : 0,
                            ErrorRate = result.TotalRequests > 0 ? Math.Round((double)result.ErrorCount / result.TotalRequests * 100, 1) : 0,
                            AvgLatency = Math.Round(result.AvgLatencyMs, 1),
                            P50 = Math.Round(result.P50LatencyMs, 1),
                            P90 = Math.Round(result.P90LatencyMs, 1),
                            P99 = Math.Round(result.P99LatencyMs, 1),
                            RequestsPerMin = result.RequestsPerMinute,
                            ComputedAt = DateTime.Now
                        }
                    }) as IActionResult;
                }
                catch
                {
                    return null;
                }
            });

            if (sqlStats != null) return sqlStats;
        }

        // Priority 3: Fallback: in-memory buffer traversal (last resort)
        var data = _memoryCache.GetOrCreate("dashboard:stats:mem", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10);
            var logSnapshot = _logQuery.GetLogs(_options.LogBufferCapacity);

            const int ParallelThreshold = 1000;
            if (logSnapshot.Entries.Count >= ParallelThreshold)
                return ComputeStatsParallel(logSnapshot);
            return ComputeStatsSequential(logSnapshot);
        })!;
        return data;
    }

    private IActionResult ComputeStatsSequential(ProxyLogStoreSnapshot snapshot)
    {
        var latencies = new List<double>(snapshot.Entries.Count / 2);
        var statusCodeCounts = new Dictionary<int, int>();
        var routeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var clusterCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int totalRequests = 0, successCount = 0, errorCount = 0;
        // Track recent requests for requestsPerMin — eliminates redundant List<DateTime> allocation
        var recentThreshold = DateTime.Now.AddMinutes(-1);
        int recentCount = 0;

        foreach (var entry in snapshot.Entries)
        {
            if (entry.EventType == LogEventType.ProxyResponse)
            {
                totalRequests++;
                var statusCode = entry.StatusCode ?? 0;
                if (statusCode >= 200 && statusCode < 400) successCount++;
                else if (statusCode >= 400) errorCount++;
                if (entry.Timestamp >= recentThreshold) recentCount++;

                CollectionsMarshalHelper.AddOrIncrement(statusCodeCounts, statusCode);
                if (entry.ElapsedMs.HasValue) latencies.Add(entry.ElapsedMs.Value);
            }
            else if (entry.EventType == LogEventType.ProxyRequest)
            {
                CollectionsMarshalHelper.AddOrIncrement(routeCounts, entry.RouteId ?? "unknown");
                CollectionsMarshalHelper.AddOrIncrement(clusterCounts, entry.ClusterId ?? "unknown");
            }
        }

        return BuildStatsResponse(totalRequests, successCount, errorCount,
            latencies, statusCodeCounts, routeCounts, clusterCounts, recentCount);
    }

    private IActionResult ComputeStatsParallel(ProxyLogStoreSnapshot snapshot)
    {
        var entries = snapshot.Entries;
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

        var recentThreshold = DateTime.Now.AddMinutes(-1);

        var results = partitions.AsParallel().Select(range =>
        {
            var localLatencies = new List<double>(range.end - range.start);
            var localStatusCodes = new Dictionary<int, int>();
            var localRoutes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var localClusters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            int localTotal = 0, localSuccess = 0, localError = 0, localRecent = 0;

            for (int i = range.start; i < range.end; i++)
            {
                var entry = entries[i];
                if (entry.EventType == LogEventType.ProxyResponse)
                {
                    localTotal++;
                    var statusCode = entry.StatusCode ?? 0;
                    if (statusCode >= 200 && statusCode < 400) localSuccess++;
                    else if (statusCode >= 400) localError++;
                    if (entry.Timestamp >= recentThreshold) localRecent++;

                    CollectionsMarshalHelper.AddOrIncrement(localStatusCodes, statusCode);
                    if (entry.ElapsedMs.HasValue) localLatencies.Add(entry.ElapsedMs.Value);
                }
                else if (entry.EventType == LogEventType.ProxyRequest)
                {
                    CollectionsMarshalHelper.AddOrIncrement(localRoutes, entry.RouteId ?? "unknown");
                    CollectionsMarshalHelper.AddOrIncrement(localClusters, entry.ClusterId ?? "unknown");
                }
            }

            return (localLatencies, localStatusCodes, localRoutes, localClusters,
                    localTotal, localSuccess, localError, localRecent);
        }).ToArray();

        var allLatencies = new List<double>(entries.Count / 2);
        var mergedStatusCodes = new Dictionary<int, int>();
        var mergedRoutes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var mergedClusters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int totalRequests = 0, successCount = 0, errorCount = 0, recentCount = 0;

        foreach (var r in results)
        {
            allLatencies.AddRange(r.localLatencies);
            MergeDictionaries(mergedStatusCodes, r.localStatusCodes);
            MergeDictionaries(mergedRoutes, r.localRoutes);
            MergeDictionaries(mergedClusters, r.localClusters);
            totalRequests += r.localTotal;
            successCount += r.localSuccess;
            errorCount += r.localError;
            recentCount += r.localRecent;
        }

        return BuildStatsResponse(totalRequests, successCount, errorCount,
            allLatencies, mergedStatusCodes, mergedRoutes, mergedClusters, recentCount);
    }

    private static void MergeDictionaries<TKey>(Dictionary<TKey, int> target, Dictionary<TKey, int> source)
        where TKey : notnull
    {
        foreach (var kvp in source)
            CollectionsMarshalHelper.AddOrIncrement(target, kvp.Key, kvp.Value);
    }

    private IActionResult BuildStatsResponse(
        int totalRequests, int successCount, int errorCount,
        List<double> latencies, Dictionary<int, int> statusCodeCounts,
        Dictionary<string, int> routeCounts, Dictionary<string, int> clusterCounts,
        int recentCount)
    {
        if (totalRequests == 0)
            return Json(new { code = 200, data = new { hasData = false } });

        double avgLatency = 0, p50 = 0, p90 = 0, p99 = 0;
        if (latencies.Count > 0)
        {
            latencies.Sort();
            avgLatency = latencies.Average();
            p50 = CalculatePercentileSorted(latencies, 0.50);
            p90 = CalculatePercentileSorted(latencies, 0.90);
            p99 = CalculatePercentileSorted(latencies, 0.99);
        }

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
            RequestsPerMin = recentCount, // Direct count instead of List<DateTime> binary search
            StatusCodes = statusCodeCounts.Select(g => new StatusCodeItem { Code = g.Key, Count = g.Value })
                .OrderByDescending(x => x.Count).ToList(),
            TopRoutes = routeCounts.Select(g => new TopItem { Name = g.Key, Count = g.Value })
                .OrderByDescending(x => x.Count).Take(10).ToList(),
            TopClusters = clusterCounts.Select(g => new TopItem { Name = g.Key, Count = g.Value })
                .OrderByDescending(x => x.Count).Take(10).ToList(),
            ComputedAt = DateTime.Now
        };

        return Json(new { code = 200, data });
    }

    private static double CalculatePercentileSorted(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        if (sorted.Count == 1) return sorted[0];

        var span = CollectionsMarshal.AsSpan(sorted);
        var idx = p * (span.Length - 1);
        var lower = (int)Math.Floor(idx);
        var upper = (int)Math.Ceiling(idx);

        if (lower == upper) return span[lower];
        return span[lower] + (span[upper] - span[lower]) * (idx - lower);
    }

    // ── DTOs for typed JSON response ──

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

    #endregion
}
