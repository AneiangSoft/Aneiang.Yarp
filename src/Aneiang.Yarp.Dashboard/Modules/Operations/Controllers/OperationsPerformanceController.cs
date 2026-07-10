using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.Operations.Controllers;

/// <summary>
/// Performance metrics — traffic time series and top issues.
/// </summary>
[Route("api/operations")]
[ApiController]
public class OperationsPerformanceController : ControllerBase
{
    private readonly IDashboardLogQueryService _logQuery;
    private readonly IMemoryCache _memoryCache;
    private readonly IProxyLogRepository _logRepository;
    private readonly DashboardOptions _options;

    public OperationsPerformanceController(
        IDashboardLogQueryService logQuery,
        IMemoryCache memoryCache,
        IProxyLogRepository logRepository,
        IOptions<DashboardOptions> options)
    {
        _logQuery = logQuery;
        _memoryCache = memoryCache;
        _logRepository = logRepository;
        _options = options.Value;
    }

    [HttpGet("traffic")]
    public IActionResult GetTrafficData([FromQuery] int minutes = 15)
    {
        var data = _memoryCache.GetOrCreate($"ops:traffic:{minutes}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10);
            return ComputeTrafficData(minutes);
        })!;
        return Ok(new { code = 200, data });
    }

    private TrafficData ComputeTrafficData(int minutes)
    {
        if (_options.LogPersistenceEnabled)
        {
            try
            {
                var sqlStartTime = DateTime.Now.AddMinutes(-minutes);
                var buckets = _logRepository.GetTrafficDataAsync(sqlStartTime).GetAwaiter().GetResult();
                if (buckets.Count == 0)
                    return new TrafficData { Labels = new(), Qps = new(), Errors = new(), CurrentQps = 0, TimeRange = minutes };

                return new TrafficData
                {
                    Labels = buckets.Select(b => b.TimeBucket.ToString("HH:mm")).ToList(),
                    Qps = buckets.Select(b => b.RequestCount).ToList(),
                    Errors = buckets.Select(b => b.ErrorCount).ToList(),
                    CurrentQps = buckets.Last().RequestCount,
                    TimeRange = minutes
                };
            }
            catch { /* fall back to in-memory */ }
        }

        var logSnapshot = _logQuery.GetLogs(_options.LogBufferCapacity);
        var entries = logSnapshot.Entries.Where(e => e.EventType == LogEventType.ProxyResponse).ToList();

        if (entries.Count == 0)
            return new TrafficData { Labels = new(), Qps = new(), Errors = new(), CurrentQps = 0, TimeRange = minutes };

        var endTime = DateTime.Now;
        var startTime = endTime.AddMinutes(-minutes);
        var interval = TimeSpan.FromMinutes(Math.Max(1, minutes / 30));

        var labels = new List<string>();
        var qpsData = new List<int>();
        var errorData = new List<int>();

        for (var time = startTime; time <= endTime; time = time.Add(interval))
        {
            var nextTime = time.Add(interval);
            var bucket = entries.Where(e => e.Timestamp >= time && e.Timestamp < nextTime).ToList();
            labels.Add(time.ToString("HH:mm"));
            qpsData.Add(bucket.Count);
            errorData.Add(bucket.Count(e => e.StatusCode >= 400));
        }

        var oneMinAgo = endTime.AddMinutes(-1);
        var currentQps = entries.Count(e => e.Timestamp >= oneMinAgo);

        return new TrafficData { Labels = labels, Qps = qpsData, Errors = errorData, CurrentQps = currentQps, TimeRange = minutes };
    }

    [HttpGet("top-issues")]
    public IActionResult GetTopIssues([FromQuery] int count = 5)
    {
        var data = _memoryCache.GetOrCreate($"ops:top-issues:{count}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            return ComputeTopIssues(count);
        })!;
        return Ok(new { code = 200, data });
    }

    private TopIssuesData ComputeTopIssues(int count)
    {
        if (_options.LogPersistenceEnabled)
        {
            try
            {
                var startTime = DateTime.Now.AddMinutes(-15);
                var issues = _logRepository.GetTopIssuesAsync(startTime, count).GetAwaiter().GetResult();
                var errorRoutes = issues.Select(i => new ErrorRouteItem
                {
                    RouteId = i.RouteId ?? "unknown",
                    ErrorCount = i.ErrorCount,
                    TotalCount = i.TotalCount,
                    RecentErrors = i.ErrorCount
                }).ToList();

                return new TopIssuesData { ErrorRoutes = errorRoutes, SlowClusters = new() };
            }
            catch { /* fall back to in-memory */ }
        }

        var logSnapshot = _logQuery.GetLogs(_options.LogBufferCapacity);
        var entries = logSnapshot.Entries.Where(e => e.EventType == LogEventType.ProxyResponse).ToList();

        if (entries.Count == 0)
            return new TopIssuesData { ErrorRoutes = new(), SlowClusters = new() };

        var routeErrors = entries
            .Where(e => e.StatusCode >= 400)
            .GroupBy(e => e.RouteId ?? "unknown")
            .Select(g => new ErrorRouteItem
            {
                RouteId = g.Key,
                ErrorCount = g.Count(),
                TotalCount = entries.Count(e => e.RouteId == g.Key),
                RecentErrors = g.Count(e => e.Timestamp >= DateTime.Now.AddMinutes(-5))
            })
            .Where(r => r.ErrorCount > 0)
            .OrderByDescending(r => r.ErrorCount)
            .Take(count)
            .ToList();

        var clusterLatencies = entries
            .Where(e => e.ElapsedMs.HasValue)
            .GroupBy(e => e.ClusterId ?? "unknown")
            .Select(g =>
            {
                var latencies = g.Select(e => e.ElapsedMs!.Value).OrderBy(x => x).ToList();
                return new SlowClusterItem
                {
                    ClusterId = g.Key,
                    AvgLatency = Math.Round(latencies.Average(), 1),
                    P50Latency = CalculatePercentileSorted(latencies, 0.50),
                    P90Latency = CalculatePercentileSorted(latencies, 0.90),
                    P99Latency = CalculatePercentileSorted(latencies, 0.99),
                    RequestCount = g.Count()
                };
            })
            .OrderByDescending(c => c.P99Latency)
            .Take(count)
            .ToList();

        return new TopIssuesData { ErrorRoutes = routeErrors, SlowClusters = clusterLatencies };
    }

    private static double CalculatePercentileSorted(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        if (sorted.Count == 1) return sorted[0];
        var idx = p * (sorted.Count - 1);
        var lower = (int)Math.Floor(idx);
        var upper = (int)Math.Ceiling(idx);
        if (lower == upper) return sorted[lower];
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (idx - lower);
    }

    private class TrafficData
    {
        public List<string> Labels { get; set; } = new();
        public List<int> Qps { get; set; } = new();
        public List<int> Errors { get; set; } = new();
        public int CurrentQps { get; set; }
        public int TimeRange { get; set; }
    }

    private class ErrorRouteItem
    {
        public string RouteId { get; set; } = string.Empty;
        public int ErrorCount { get; set; }
        public int TotalCount { get; set; }
        public int RecentErrors { get; set; }
        public double ErrorRate => TotalCount > 0 ? Math.Round((double)ErrorCount / TotalCount * 100, 1) : 0;
    }

    private class SlowClusterItem
    {
        public string ClusterId { get; set; } = string.Empty;
        public double AvgLatency { get; set; }
        public double P50Latency { get; set; }
        public double P90Latency { get; set; }
        public double P99Latency { get; set; }
        public int RequestCount { get; set; }
    }

    private class TopIssuesData
    {
        public List<ErrorRouteItem> ErrorRoutes { get; set; } = new();
        public List<SlowClusterItem> SlowClusters { get; set; } = new();
    }
}
