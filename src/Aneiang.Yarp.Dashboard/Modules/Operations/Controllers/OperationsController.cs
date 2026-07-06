using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Performance;
using Aneiang.Yarp.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.Operations.Controllers;

/// <summary>
/// Operations monitoring API - provides aggregated data for DevOps dashboard.
/// 运维监控API - 为运维监控台提供聚合数据
/// </summary>
[Route("api/operations")]
[ApiController]
public class OperationsController : ControllerBase
{
    private readonly IDashboardLogQueryService _logQuery;
    private readonly IDashboardClusterQueryService _clusterQuery;
    private readonly IMemoryCache _memoryCache;
    private readonly IProxyLogRepository _logRepository;
    private readonly LockFreeStatistics _statistics;
    private readonly DashboardOptions _options;

    public OperationsController(
        IDashboardLogQueryService logQuery,
        IDashboardClusterQueryService clusterQuery,
        IMemoryCache memoryCache,
        IProxyLogRepository logRepository,
        LockFreeStatistics statistics,
        IOptions<DashboardOptions> options)
    {
        _logQuery = logQuery;
        _clusterQuery = clusterQuery;
        _memoryCache = memoryCache;
        _logRepository = logRepository;
        _statistics = statistics;
        _options = options.Value;
    }

    /// <summary>
    /// Get alert summary for top alert bar. Cached for 10 seconds.
    /// 获取告警摘要数据（顶部告警栏）
    /// </summary>
    [HttpGet("alert-summary")]
    public IActionResult GetAlertSummary()
    {
        var data = _memoryCache.GetOrCreate("ops:alert-summary", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10);
            return ComputeAlertSummary();
        })!;
        return Ok(new { code = 200, data });
    }

    private AlertSummaryData ComputeAlertSummary()
    {
        var clusters = _clusterQuery.GetClusters();

        // Count unhealthy destinations
        int unhealthyCount = 0;
        int totalDestinations = 0;
        foreach (var cluster in clusters)
        {
            if (cluster.Destinations != null)
            {
                foreach (var dest in cluster.Destinations)
                {
                    totalDestinations++;
                    if (IsUnhealthy(dest.Health))
                    {
                        unhealthyCount++;
                    }
                }
            }
        }

        // Calculate recent 5xx errors — priority: LockFreeStatistics → SQL → in-memory
        int recentErrors;
        var statsSnapshot = _statistics.GetSnapshot();

        // Sum all 5xx status codes from LockFreeStatistics (cumulative, fast O(1) read)
        var total5xx = statsSnapshot.StatusCodes
            .Where(kvp => kvp.Key >= 500)
            .Sum(kvp => kvp.Value);

        if (_options.LogPersistenceEnabled)
        {
            // SQL gives us recent-5min count (more accurate for "recent" window)
            try { recentErrors = _logRepository.GetRecent5xxCountAsync(5).GetAwaiter().GetResult(); }
            catch { recentErrors = (int)total5xx; }
        }
        else
        {
            // No persistence: use in-memory buffer for recent window
            var logSnapshot = _logQuery.GetLogs(1000);
            var fiveMinAgo = DateTime.Now.AddMinutes(-5);
            recentErrors = logSnapshot.Entries.Count(e =>
                e.EventType == LogEventType.ProxyResponse &&
                e.Timestamp >= fiveMinAgo &&
                e.StatusCode >= 500);
        }

        // Get circuit breaker status
        int circuitBreakerCount = CountCircuitBreakers();

        return new AlertSummaryData
        {
            UnhealthyCount = unhealthyCount,
            UnhealthyTotal = totalDestinations,
            CircuitBreakerCount = circuitBreakerCount,
            RecentErrors = recentErrors,
            UnhandledEvents = recentErrors + unhealthyCount,
            LastUpdated = DateTime.Now
        };
    }

    /// <summary>
    /// Get traffic time series data for charts. Cached for 10 seconds.
    /// 获取流量时序数据（用于图表）
    /// </summary>
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
        // Prefer SQL aggregation when persistence is enabled
        if (_options.LogPersistenceEnabled)
        {
            try
            {
                var sqlStartTime = DateTime.Now.AddMinutes(-minutes);
                var buckets = _logRepository.GetTrafficDataAsync(sqlStartTime).GetAwaiter().GetResult();
                if (buckets.Count == 0)
                {
                    return new TrafficData
                    {
                        Labels = Array.Empty<string>().ToList(),
                        Qps = Array.Empty<int>().ToList(),
                        Errors = Array.Empty<int>().ToList(),
                        CurrentQps = 0,
                        TimeRange = minutes
                    };
                }

                return new TrafficData
                {
                    Labels = buckets.Select(b => b.TimeBucket.ToString("HH:mm")).ToList(),
                    Qps = buckets.Select(b => b.RequestCount).ToList(),
                    Errors = buckets.Select(b => b.ErrorCount).ToList(),
                    CurrentQps = buckets.Count > 0 ? buckets.Last().RequestCount : 0,
                    TimeRange = minutes
                };
            }
            catch { /* fall back to in-memory */ }
        }

        // Fallback: in-memory buffer traversal
        var logSnapshot = _logQuery.GetLogs(_options.LogBufferCapacity);
        var entries = logSnapshot.Entries.Where(e => e.EventType == LogEventType.ProxyResponse).ToList();
        
        if (entries.Count == 0)
        {
            return new TrafficData
            {
                Labels = Array.Empty<string>().ToList(),
                Qps = Array.Empty<int>().ToList(),
                Errors = Array.Empty<int>().ToList(),
                CurrentQps = 0,
                TimeRange = minutes
            };
        }

        var endTime = DateTime.Now;
        var startTime = endTime.AddMinutes(-minutes);
        var interval = TimeSpan.FromMinutes(Math.Max(1, minutes / 30)); // 30 data points max
        
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

        // Calculate current QPS (last minute)
        var oneMinAgo = endTime.AddMinutes(-1);
        var currentQps = entries.Count(e => e.Timestamp >= oneMinAgo);

        return new TrafficData
        {
            Labels = labels,
            Qps = qpsData,
            Errors = errorData,
            CurrentQps = currentQps,
            TimeRange = minutes
        };
    }

    /// <summary>
    /// Get top error routes and slow clusters for overview page. Cached for 30 seconds.
    /// 获取异常路由和延迟集群排行（Overview页面）
    /// </summary>
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
        // Prefer SQL aggregation when persistence is enabled
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
                    RecentErrors = i.ErrorCount // Approximation — SQL data is already filtered by time
                }).ToList();

                return new TopIssuesData
                {
                    ErrorRoutes = errorRoutes,
                    SlowClusters = new List<SlowClusterItem>() // P99 latency needs separate query — placeholder
                };
            }
            catch { /* fall back to in-memory */ }
        }

        // Fallback: in-memory buffer traversal
        var logSnapshot = _logQuery.GetLogs(_options.LogBufferCapacity);
        var entries = logSnapshot.Entries.Where(e => e.EventType == LogEventType.ProxyResponse).ToList();
        
        if (entries.Count == 0)
        {
            return new TopIssuesData
            {
                ErrorRoutes = new List<ErrorRouteItem>(),
                SlowClusters = new List<SlowClusterItem>()
            };
        }

        // Top error routes
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

        // Top slow clusters by P99 latency
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

        return new TopIssuesData
        {
            ErrorRoutes = routeErrors,
            SlowClusters = clusterLatencies
        };
    }

    /// <summary>
    /// Get health score and summary for all clusters.
    /// 获取所有集群的健康评分和摘要
    /// </summary>
    [HttpGet("health-summary")]
    public IActionResult GetHealthSummary()
    {
        var clusters = _clusterQuery.GetClusters();
        var totalDestinations = 0;
        var healthyDestinations = 0;
        var unhealthyDestinations = 0;
        var unknownDestinations = 0;

        foreach (var cluster in clusters)
        {
            if (cluster.Destinations != null)
            {
                foreach (var dest in cluster.Destinations)
                {
                    totalDestinations++;
                    var health = dest.Health;
                    if (string.IsNullOrEmpty(health))
                    {
                        unknownDestinations++;
                    }
                    else if (health.Equals("Healthy", StringComparison.OrdinalIgnoreCase))
                    {
                        healthyDestinations++;
                    }
                    else if (health.Equals("Unhealthy", StringComparison.OrdinalIgnoreCase))
                    {
                        unhealthyDestinations++;
                    }
                    else
                    {
                        unknownDestinations++;
                    }
                }
            }
        }

        var healthScore = totalDestinations > 0 
            ? Math.Round((double)healthyDestinations / totalDestinations * 100, 1) 
            : 100;

        var data = new HealthSummaryData
        {
            HealthScore = healthScore,
            TotalClusters = clusters.Count,
            TotalDestinations = totalDestinations,
            HealthyCount = healthyDestinations,
            UnhealthyCount = unhealthyDestinations,
            UnknownCount = unknownDestinations,
            Status = healthScore >= 90 ? "Healthy" : healthScore >= 70 ? "Warning" : "Critical"
        };

        return Ok(new { code = 200, data });
    }

    /// <summary>
    /// Export current system snapshot.
    /// 导出系统快照
    /// </summary>
    [HttpGet("snapshot")]
    public IActionResult ExportSnapshot()
    {
        var clusters = _clusterQuery.GetClusters();
        var snapshot = new SystemSnapshot
        {
            ExportedAt = DateTime.Now,
            ClusterCount = clusters.Count,
            RouteCount = 0, // Routes not available in this context
            Clusters = clusters.Select(c => new ClusterSnapshot 
            { 
                Id = c.ClusterId, 
                DestinationCount = c.Destinations?.Count ?? 0 
            }).ToList(),
            Routes = new List<RouteSnapshot>()
        };

        return Ok(new { code = 200, data = snapshot });
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

    private int CountCircuitBreakers()
    {
        // Query real circuit breaker states from CircuitBreakerMiddleware
        var allStates = CircuitBreakerMiddleware.GetAllCircuitStates();
        int openCount = 0;
        foreach (var state in allStates)
        {
            if (state.Status == "Open" || state.Status == "HalfOpen")
                openCount++;
        }
        return openCount;
    }

    private static bool IsUnhealthy(string? health)
    {
        return !string.IsNullOrEmpty(health) && 
               health.Equals("Unhealthy", StringComparison.OrdinalIgnoreCase);
    }

    // DTOs
    private class AlertSummaryData
    {
        public int UnhealthyCount { get; set; }
        public int UnhealthyTotal { get; set; }
        public int CircuitBreakerCount { get; set; }
        public int RecentErrors { get; set; }
        public int UnhandledEvents { get; set; }
        public DateTime LastUpdated { get; set; }
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

    private class HealthSummaryData
    {
        public double HealthScore { get; set; }
        public int TotalClusters { get; set; }
        public int TotalDestinations { get; set; }
        public int HealthyCount { get; set; }
        public int UnhealthyCount { get; set; }
        public int UnknownCount { get; set; }
        public string Status { get; set; } = "Healthy";
    }

    private class SystemSnapshot
    {
        public DateTime ExportedAt { get; set; }
        public int ClusterCount { get; set; }
        public int RouteCount { get; set; }
        public List<ClusterSnapshot> Clusters { get; set; } = new();
        public List<RouteSnapshot> Routes { get; set; } = new();
    }

    private class ClusterSnapshot
    {
        public string Id { get; set; } = string.Empty;
        public int DestinationCount { get; set; }
    }

    private class RouteSnapshot
    {
        public string Id { get; set; } = string.Empty;
        public string? ClusterId { get; set; }
    }
}