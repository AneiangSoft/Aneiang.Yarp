using System.Runtime.InteropServices;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Infrastructure.Performance;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Microsoft.AspNetCore.Mvc;
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
        IOptions<DashboardOptions> dashboardOptions)
    {
        _infoQuery = infoQuery;
        _clusterQuery = clusterQuery;
        _routeQuery = routeQuery;
        _logQuery = logQuery;
        _authService = authService;

        var opt = dashboardOptions.Value;
        _defaultLocale = opt.Locale;
        _authMode = opt.AuthMode;
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

    // ── Stats ──

    /// <summary>Access statistics computed from the log buffer.</summary>
    [HttpGet("api/stats")]
    public IActionResult GetStats()
    {
        var snapshot = _logQuery.GetLogs(2000);

        const int ParallelThreshold = 1000;
        if (snapshot.Entries.Count >= ParallelThreshold)
            return ComputeStatsParallel(snapshot);
        return ComputeStatsSequential(snapshot);
    }

    private IActionResult ComputeStatsSequential(ProxyLogStoreSnapshot snapshot)
    {
        var latencies = new List<double>(snapshot.Entries.Count / 2);
        var statusCodeCounts = new Dictionary<int, int>();
        var routeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var clusterCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int totalRequests = 0, successCount = 0, errorCount = 0;
        DateTime maxResponseTimestamp = DateTime.MinValue;

        foreach (var entry in snapshot.Entries)
        {
            if (entry.EventType == LogEventType.ProxyResponse)
            {
                totalRequests++;
                var statusCode = entry.StatusCode ?? 0;
                if (statusCode >= 200 && statusCode < 400) successCount++;
                else if (statusCode >= 400) errorCount++;

                CollectionsMarshalHelper.AddOrIncrement(statusCodeCounts, statusCode);
                if (entry.ElapsedMs.HasValue) latencies.Add(entry.ElapsedMs.Value);
                if (entry.Timestamp > maxResponseTimestamp) maxResponseTimestamp = entry.Timestamp;
            }
            else if (entry.EventType == LogEventType.ProxyRequest)
            {
                CollectionsMarshalHelper.AddOrIncrement(routeCounts, entry.RouteId ?? "unknown");
                CollectionsMarshalHelper.AddOrIncrement(clusterCounts, entry.ClusterId ?? "unknown");
            }
        }

        return BuildStatsResponse(totalRequests, successCount, errorCount,
            latencies, statusCodeCounts, routeCounts, clusterCounts,
            maxResponseTimestamp, snapshot.Entries);
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

        return BuildStatsResponse(totalRequests, successCount, errorCount,
            allLatencies, mergedStatusCodes, mergedRoutes, mergedClusters,
            maxResponseTimestamp, entries);
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
        DateTime maxResponseTimestamp, List<LogEntry> entries)
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

        int requestsPerMin = 0;
        if (maxResponseTimestamp > DateTime.MinValue)
        {
            var oneMinAgo = maxResponseTimestamp.AddMinutes(-1);
            var responseTimestamps = new List<DateTime>(totalRequests);
            foreach (var e in entries)
                if (e.EventType == LogEventType.ProxyResponse)
                    responseTimestamps.Add(e.Timestamp);

            if (responseTimestamps.Count > 0)
            {
                responseTimestamps.Sort();
                int lo = 0, hi = responseTimestamps.Count;
                while (lo < hi)
                {
                    int mid = lo + (hi - lo) / 2;
                    if (responseTimestamps[mid] < oneMinAgo) lo = mid + 1;
                    else hi = mid;
                }
                requestsPerMin = responseTimestamps.Count - lo;
            }
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
            RequestsPerMin = requestsPerMin,
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
