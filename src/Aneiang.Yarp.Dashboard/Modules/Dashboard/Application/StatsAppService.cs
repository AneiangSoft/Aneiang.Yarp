using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Common;
using Aneiang.Yarp.Dashboard.Infrastructure.Performance;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Dtos;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Application;

/// <summary>
/// Application service for computing dashboard statistics.
/// Encapsulates multi-tier priority logic (lock-free stats -> SQL -> in-memory)
/// previously embedded in <see cref="Controllers.DashboardStatsController"/>.
/// </summary>
public interface IStatsAppService
{
    Task<StatsData?> GetStatsAsync();
}

/// <inheritdoc/>
public class StatsAppService : IStatsAppService
{
    private readonly IDashboardLogQueryService _logQuery;
    private readonly IMemoryCache _memoryCache;
    private readonly IProxyLogRepository _logRepository;
    private readonly LockFreeStatistics _statistics;
    private readonly DashboardOptions _options;

    public StatsAppService(
        IDashboardLogQueryService logQuery,
        IMemoryCache memoryCache,
        IProxyLogRepository logRepository,
        LockFreeStatistics statistics,
        IOptions<DashboardOptions> dashboardOptions)
    {
        _logQuery = logQuery;
        _memoryCache = memoryCache;
        _logRepository = logRepository;
        _statistics = statistics;
        _options = dashboardOptions.Value;
    }

    public async Task<StatsData?> GetStatsAsync()
    {
        // Priority 1: Lock-free statistics snapshot
        var snapshot = _statistics.GetSnapshot();
        if (snapshot.TotalRequests > 0)
        {
            var cached = await _memoryCache.GetOrCreateAsync("dashboard:stats:lfs", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10);
                var avgLatencyMs = snapshot.AvgLatencyMicros / 1000.0;
                var recentThreshold = DateTime.Now.AddMinutes(-1);
                var requestsPerMin = snapshot.ComputedAt >= recentThreshold
                    ? (int)(snapshot.TotalRequests / Math.Max(1, (DateTime.Now - snapshot.ComputedAt).TotalMinutes))
                    : 0;

                return new StatsData
                {
                    HasData = true,
                    TotalRequests = (int)snapshot.TotalRequests,
                    SuccessCount = (int)snapshot.SuccessCount,
                    ErrorCount = (int)snapshot.ErrorCount,
                    SuccessRate = snapshot.SuccessRate,
                    ErrorRate = snapshot.ErrorRate,
                    AvgLatency = Math.Round(avgLatencyMs, 1),
                    P50 = Math.Round(avgLatencyMs * 0.85, 1),
                    P90 = Math.Round(avgLatencyMs * 1.5, 1),
                    P99 = Math.Round(avgLatencyMs * 2.5, 1),
                    RequestsPerMin = requestsPerMin,
                    StatusCodes = snapshot.StatusCodes.Select(g => new StatusCodeItem { Code = g.Key, Count = (int)g.Value })
                        .OrderByDescending(x => x.Count).ToList(),
                    TopRoutes = snapshot.TopRoutes.Select(g => new TopItem { Name = $"route:{g.Key}", Count = (int)g.Value })
                        .OrderByDescending(x => x.Count).Take(10).ToList(),
                    TopClusters = snapshot.TopClusters.Select(g => new TopItem { Name = $"cluster:{g.Key}", Count = (int)g.Value })
                        .OrderByDescending(x => x.Count).Take(10).ToList(),
                    ComputedAt = snapshot.ComputedAt
                };
            });
            if (cached != null) return cached;
        }

        // Priority 2: SQL aggregation
        if (_options.LogPersistenceEnabled)
        {
            var sqlStats = await _memoryCache.GetOrCreateAsync("dashboard:stats", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10);
                try
                {
                    var result = await _logRepository.GetStatsAsync(5);
                    if (result.TotalRequests == 0)
                        return new StatsData { HasData = false };

                    return new StatsData
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
                    };
                }
                catch
                {
                    return null;
                }
            });
            if (sqlStats != null) return sqlStats;
        }

        // Priority 3: In-memory buffer traversal
        var data = await _memoryCache.GetOrCreateAsync("dashboard:stats:mem", async entry =>
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

    private StatsData ComputeStatsSequential(ProxyLogStoreSnapshot snapshot)
    {
        var latencies = new List<double>(snapshot.Entries.Count / 2);
        var statusCodeCounts = new Dictionary<int, int>();
        var routeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var clusterCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int totalRequests = 0, successCount = 0, errorCount = 0;
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

        return BuildStatsData(totalRequests, successCount, errorCount,
            latencies, statusCodeCounts, routeCounts, clusterCounts, recentCount);
    }

    private StatsData ComputeStatsParallel(ProxyLogStoreSnapshot snapshot)
    {
        var entries = snapshot.Entries;
        var processorCount = Environment.ProcessorCount;
        var partitionSize = Math.Max(1, entries.Count / processorCount);
        var partitions = Enumerable.Range(0, processorCount)
            .Select(i => { var start = i * partitionSize; var end = (i == processorCount - 1) ? entries.Count : Math.Min(start + partitionSize, entries.Count); return (start, end); })
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
            return (localLatencies, localStatusCodes, localRoutes, localClusters, localTotal, localSuccess, localError, localRecent);
        }).ToArray();

        var allLatencies = new List<double>(entries.Count / 2);
        var mergedStatusCodes = new Dictionary<int, int>();
        var mergedRoutes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var mergedClusters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int totalRequests = 0, successCount = 0, errorCount = 0, recentCount = 0;

        foreach (var r in results)
        {
            allLatencies.AddRange(r.localLatencies);
            StatisticsHelper.MergeDictionaries(mergedStatusCodes, r.localStatusCodes);
            StatisticsHelper.MergeDictionaries(mergedRoutes, r.localRoutes);
            StatisticsHelper.MergeDictionaries(mergedClusters, r.localClusters);
            totalRequests += r.localTotal;
            successCount += r.localSuccess;
            errorCount += r.localError;
            recentCount += r.localRecent;
        }

        return BuildStatsData(totalRequests, successCount, errorCount,
            allLatencies, mergedStatusCodes, mergedRoutes, mergedClusters, recentCount);
    }

    private static StatsData BuildStatsData(
        int totalRequests, int successCount, int errorCount,
        List<double> latencies, Dictionary<int, int> statusCodeCounts,
        Dictionary<string, int> routeCounts, Dictionary<string, int> clusterCounts, int recentCount)
    {
        if (totalRequests == 0)
            return new StatsData { HasData = false };

        double avgLatency = 0, p50 = 0, p90 = 0, p99 = 0;
        if (latencies.Count > 0)
        {
            latencies.Sort();
            avgLatency = latencies.Average();
            p50 = StatisticsHelper.CalculatePercentileSorted(latencies, 0.50);
            p90 = StatisticsHelper.CalculatePercentileSorted(latencies, 0.90);
            p99 = StatisticsHelper.CalculatePercentileSorted(latencies, 0.99);
        }

        return new StatsData
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
            RequestsPerMin = recentCount,
            StatusCodes = statusCodeCounts.Select(g => new StatusCodeItem { Code = g.Key, Count = g.Value })
                .OrderByDescending(x => x.Count).ToList(),
            TopRoutes = routeCounts.Select(g => new TopItem { Name = g.Key, Count = g.Value })
                .OrderByDescending(x => x.Count).Take(10).ToList(),
            TopClusters = clusterCounts.Select(g => new TopItem { Name = g.Key, Count = g.Value })
                .OrderByDescending(x => x.Count).Take(10).ToList(),
            ComputedAt = DateTime.Now
        };
    }
}
