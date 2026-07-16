using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Common;
using Aneiang.Yarp.Dashboard.Modules.Operations.Dtos;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.Operations.Application;

/// <summary>
/// Application service for computing performance metrics (traffic, top issues).
/// Previously embedded in <see cref="Controllers.OperationsPerformanceController"/>.
/// </summary>
public interface IPerformanceAppService
{
    Task<TrafficData> ComputeTrafficDataAsync(int minutes);
    Task<TopIssuesData> ComputeTopIssuesAsync(int count);
}

/// <inheritdoc/>
public class PerformanceAppService : IPerformanceAppService
{
    private readonly IDashboardLogQueryService _logQuery;
    private readonly IProxyLogRepository _logRepository;
    private readonly DashboardOptions _options;

    public PerformanceAppService(
        IDashboardLogQueryService logQuery,
        IProxyLogRepository logRepository,
        IOptions<DashboardOptions> options)
    {
        _logQuery = logQuery;
        _logRepository = logRepository;
        _options = options.Value;
    }

    public async Task<TrafficData> ComputeTrafficDataAsync(int minutes)
    {
        if (_options.LogPersistenceEnabled)
        {
            try
            {
                var sqlStartTime = DateTime.Now.AddMinutes(-minutes);
                var buckets = await _logRepository.GetTrafficDataAsync(sqlStartTime);
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

    public async Task<TopIssuesData> ComputeTopIssuesAsync(int count)
    {
        if (_options.LogPersistenceEnabled)
        {
            try
            {
                var startTime = DateTime.Now.AddMinutes(-15);
                var issues = await _logRepository.GetTopIssuesAsync(startTime, count);
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
                    P50Latency = StatisticsHelper.CalculatePercentileSorted(latencies, 0.50),
                    P90Latency = StatisticsHelper.CalculatePercentileSorted(latencies, 0.90),
                    P99Latency = StatisticsHelper.CalculatePercentileSorted(latencies, 0.99),
                    RequestCount = g.Count()
                };
            })
            .OrderByDescending(c => c.P99Latency)
            .Take(count)
            .ToList();

        return new TopIssuesData { ErrorRoutes = routeErrors, SlowClusters = clusterLatencies };
    }
}
