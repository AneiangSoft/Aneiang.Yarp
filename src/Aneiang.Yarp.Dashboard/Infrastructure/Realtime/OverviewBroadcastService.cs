using Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Realtime;

internal sealed class OverviewBroadcastService : BackgroundService
{
    private readonly IHubContext<OverviewHub> _hubContext;
    private readonly IDashboardClusterQueryService _clusterQuery;
    private readonly IDashboardRouteQueryService _routeQuery;
    private readonly IDashboardInfoQueryService _infoQuery;
    private readonly IProxyLogStore _logStore;
    private readonly ILogger<OverviewBroadcastService> _logger;

    private static readonly TimeSpan BroadcastInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TrafficWindow = TimeSpan.FromSeconds(60);

    public OverviewBroadcastService(
        IHubContext<OverviewHub> hubContext,
        IDashboardClusterQueryService clusterQuery,
        IDashboardRouteQueryService routeQuery,
        IDashboardInfoQueryService infoQuery,
        IProxyLogStore logStore,
        ILogger<OverviewBroadcastService> logger)
    {
        _hubContext = hubContext;
        _clusterQuery = clusterQuery;
        _routeQuery = routeQuery;
        _infoQuery = infoQuery;
        _logStore = logStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug(
            "OverviewBroadcastService started — broadcasting every {Interval}s",
            BroadcastInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(BroadcastInterval, stoppingToken);
                await CollectAndBroadcastAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OverviewBroadcastService iteration failed");
            }
        }
    }

    private async Task CollectAndBroadcastAsync(CancellationToken ct)
    {
        // --- Cluster + health distribution ---
        int clusterCount = 0, healthyCount = 0, unknownCount = 0, unhealthyCount = 0;
        try
        {
            var clusters = _clusterQuery.GetClusters();
            clusterCount = clusters.Count;
            foreach (var c in clusters)
            {
                healthyCount += c.HealthyCount;
                unknownCount += c.UnknownCount;
                unhealthyCount += c.UnhealthyCount;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to query clusters for overview broadcast");
        }

        // --- Route count ---
        int routeCount = 0;
        try
        {
            routeCount = _routeQuery.GetRoutes().Count;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to query routes for overview broadcast");
        }

        // --- System metrics ---
        double cpuUsage = 0;
        long memoryMb = 0;
        int gcCount = 0, threadCount = 0;
        try
        {
            var info = _infoQuery.GetInfo();
            cpuUsage = info.CpuUsage;
            memoryMb = (long)info.MemoryMb;
            gcCount = info.GcCount;
            threadCount = info.ThreadCount;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to query system info for overview broadcast");
        }

        // --- QPS + top errors + slow clusters (from proxy log store) ---
        double currentQps = 0;
        List<OverviewErrorRoute> topErrorRoutes = new();
        List<OverviewSlowCluster> topSlowClusters = new();
        try
        {
            var snapshot = _logStore.GetRecent(500);
            var cutoff = DateTime.Now - TrafficWindow;
            var relevantEntries = new List<LogEntry>();

            foreach (var entry in snapshot.Entries)
            {
                if (entry.EventType == LogEventType.ProxyResponse && entry.Timestamp >= cutoff)
                    relevantEntries.Add(entry);
            }

            if (relevantEntries.Count > 0)
            {
                currentQps = Math.Round(relevantEntries.Count / TrafficWindow.TotalSeconds, 2);

                // Top error routes (by error count in the last 60s window)
                var routeErrors = relevantEntries
                    .Where(e => e.StatusCode >= 400)
                    .GroupBy(e => e.RouteId ?? "unknown")
                    .Select(g => new
                    {
                        RouteId = g.Key,
                        ErrorCount = g.Count(),
                        TotalCount = relevantEntries.Count(e => (e.RouteId ?? "unknown") == g.Key)
                    })
                    .Where(r => r.ErrorCount > 0)
                    .OrderByDescending(r => r.ErrorCount)
                    .Take(5)
                    .Select(r => new OverviewErrorRoute
                    {
                        RouteId = r.RouteId,
                        ErrorCount = r.ErrorCount,
                        ErrorRate = r.TotalCount > 0
                            ? Math.Round((double)r.ErrorCount / r.TotalCount * 100, 1)
                            : 0
                    })
                    .ToList();

                topErrorRoutes = routeErrors;

                // Top slow clusters (by P99 latency in the last 60s window)
                var clusterLatencies = relevantEntries
                    .Where(e => e.ElapsedMs.HasValue)
                    .GroupBy(e => e.ClusterId ?? "unknown")
                    .Select(g =>
                    {
                        var latencies = g.Select(e => e.ElapsedMs!.Value).OrderBy(x => x).ToList();
                        return new OverviewSlowCluster
                        {
                            ClusterId = g.Key,
                            AvgLatency = Math.Round(latencies.Average(), 1),
                            P99Latency = CalculateP99(latencies)
                        };
                    })
                    .OrderByDescending(c => c.P99Latency)
                    .Take(5)
                    .ToList();

                topSlowClusters = clusterLatencies;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to compute traffic metrics for overview broadcast");
        }

        var overviewSnapshot = new OverviewSnapshot
        {
            ClusterCount = clusterCount,
            RouteCount = routeCount,
            HealthyCount = healthyCount,
            UnknownCount = unknownCount,
            UnhealthyCount = unhealthyCount,
            CurrentQps = currentQps,
            CpuUsage = cpuUsage,
            MemoryMb = memoryMb,
            GcCount = gcCount,
            ThreadCount = threadCount,
            TopErrorRoutes = topErrorRoutes,
            TopSlowClusters = topSlowClusters,
            Timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds()
        };

        await _hubContext.Clients.Group("overview").SendCoreAsync(
            "OverviewUpdate",
            new object[] { overviewSnapshot },
            ct);
    }

    private static double CalculateP99(List<double> sorted)
    {
        if (sorted.Count == 0) return 0;
        if (sorted.Count == 1) return sorted[0];
        var idx = 0.99 * (sorted.Count - 1);
        var lower = (int)Math.Floor(idx);
        var upper = (int)Math.Ceiling(idx);
        if (lower == upper) return sorted[lower];
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (idx - lower);
    }
}
