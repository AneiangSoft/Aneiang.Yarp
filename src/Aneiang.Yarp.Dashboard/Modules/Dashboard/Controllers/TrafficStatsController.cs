using Aneiang.Yarp.Plugin.ProxyLog.Models;
using Aneiang.Yarp.Plugin.ProxyLog.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>
/// Provides aggregated traffic statistics derived from the in-memory proxy log store.
/// Replaces the deleted DashboardStatsController and Operations controllers.
/// </summary>
public class TrafficStatsController : Controller
{
    private readonly IProxyLogStore _logStore;

    public TrafficStatsController(IProxyLogStore logStore)
    {
        _logStore = logStore;
    }

    /// <summary>Aggregated traffic summary + time-series for the traffic monitor page.</summary>
    [HttpGet("api/traffic/stats")]
    public IActionResult GetTrafficStats([FromQuery] int minutes = 60)
    {
        var cutoff = DateTime.Now.AddMinutes(-Math.Min(minutes, 10080));
        var snapshot = _logStore.GetRecent(5000);
        var responses = snapshot.Entries
            .Where(e => e.EventType == LogEventType.ProxyResponse && e.Timestamp >= cutoff)
            .ToList();

        var total = responses.Count;
        var success = responses.Count(r => r.StatusCode.HasValue && r.StatusCode.Value < 400);
        var errors = responses.Count(r => r.StatusCode.HasValue && r.StatusCode.Value >= 400);
        var latencyValues = responses
            .Where(r => r.ElapsedMs.HasValue)
            .Select(r => r.ElapsedMs!.Value)
            .OrderBy(v => v)
            .ToList();

        // Time buckets (1-minute granularity)
        var bucketCount = Math.Min(minutes, 60);
        var bucketSize = minutes / bucketCount;
        var buckets = new List<TrafficBucket>();
        for (int i = bucketCount - 1; i >= 0; i--)
        {
            var bucketStart = DateTime.Now.AddMinutes(-(i + 1) * bucketSize);
            var bucketEnd = DateTime.Now.AddMinutes(-i * bucketSize);
            var bucketEntries = responses.Where(e => e.Timestamp >= bucketStart && e.Timestamp < bucketEnd).ToList();
            buckets.Add(new TrafficBucket
            {
                Time = bucketStart.ToString("HH:mm"),
                Requests = bucketEntries.Count,
                Errors = bucketEntries.Count(e => e.StatusCode.HasValue && e.StatusCode.Value >= 400),
                AvgLatency = bucketEntries.Where(e => e.ElapsedMs.HasValue).Select(e => e.ElapsedMs!.Value).DefaultIfEmpty(0).Average()
            });
        }

        // Status code distribution
        var statusGroups = responses
            .Where(r => r.StatusCode.HasValue)
            .GroupBy(r => r.StatusCode!.Value / 100)
            .OrderBy(g => g.Key)
            .ToDictionary(g => $"{g.Key}xx", g => g.Count());

        // Latency percentiles
        double p50 = Percentile(latencyValues, 0.50);
        double p90 = Percentile(latencyValues, 0.90);
        double p99 = Percentile(latencyValues, 0.99);

        // Top routes
        var topRoutes = responses
            .Where(r => !string.IsNullOrEmpty(r.RouteId))
            .GroupBy(r => r.RouteId!)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new TopItem { Name = g.Key, Count = g.Count(), AvgLatency = g.Where(e => e.ElapsedMs.HasValue).Select(e => e.ElapsedMs!.Value).DefaultIfEmpty(0).Average() })
            .ToList();

        // Top clusters
        var topClusters = responses
            .Where(r => !string.IsNullOrEmpty(r.ClusterId))
            .GroupBy(r => r.ClusterId!)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new TopItem { Name = g.Key, Count = g.Count(), AvgLatency = g.Where(e => e.ElapsedMs.HasValue).Select(e => e.ElapsedMs!.Value).DefaultIfEmpty(0).Average() })
            .ToList();

        return Json(new
        {
            code = 200,
            data = new
            {
                totalRequests = total,
                successRate = total > 0 ? Math.Round((double)success / total * 100, 1) : 0,
                errorCount = errors,
                avgLatency = latencyValues.Count > 0 ? Math.Round(latencyValues.Average(), 1) : 0,
                rpm = total > 0 && minutes > 0 ? Math.Round((double)total / minutes, 1) : 0,
                currentQps = buckets.Count > 0 ? buckets.Last().Requests / (double)bucketSize : 0,
                buckets,
                statusCodes = statusGroups,
                percentiles = new { p50 = Math.Round(p50, 1), p90 = Math.Round(p90, 1), p99 = Math.Round(p99, 1) },
                topRoutes,
                topClusters,
                bufferSize = snapshot.BufferSize,
                bufferCapacity = snapshot.BufferCapacity
            }
        });
    }

    private static double Percentile(List<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0) return 0;
        var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
        return sortedValues[Math.Max(0, Math.Min(index, sortedValues.Count - 1))];
    }

    private class TrafficBucket
    {
        public string Time { get; set; } = "";
        public int Requests { get; set; }
        public int Errors { get; set; }
        public double AvgLatency { get; set; }
    }

    private class TopItem
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
        public double AvgLatency { get; set; }
    }
}
