using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Realtime;

/// <summary>
/// Background service that collects real-time traffic metrics from the proxy log store
/// and broadcasts them to connected SignalR clients for live topology animation.
/// Uses a sliding window over the ring buffer to compute per-route traffic statistics
/// (RPS, error rate, latency) and pushes updates every 2 seconds.
/// </summary>
public sealed class TrafficBroadcastService : BackgroundService
{
    private readonly IHubContext<TrafficHub> _hubContext;
    private readonly IProxyLogStore _logStore;
    private readonly ILogger<TrafficBroadcastService> _logger;

    private readonly TimeSpan _windowDuration = TimeSpan.FromSeconds(60);
    private readonly TimeSpan _broadcastInterval = TimeSpan.FromSeconds(2);

    public TrafficBroadcastService(
        IHubContext<TrafficHub> hubContext,
        IProxyLogStore logStore,
        ILogger<TrafficBroadcastService> logger)
    {
        _hubContext = hubContext;
        _logStore = logStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "TrafficBroadcastService started — broadcasting every {Interval}s",
            _broadcastInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_broadcastInterval, stoppingToken);
                await CollectAndBroadcastAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TrafficBroadcastService iteration failed");
            }
        }
    }

    private async Task CollectAndBroadcastAsync(CancellationToken ct)
    {
        var snapshot = _logStore.GetRecent(500);
        var cutoff = DateTime.UtcNow - _windowDuration;

        var routeMetrics = new Dictionary<string, RouteMetrics>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in snapshot.Entries)
        {
            if (entry.EventType != LogEventType.ProxyResponse)
                continue;
            if (entry.Timestamp < cutoff)
                continue;

            var routeId = entry.RouteId ?? "unknown";

            if (!routeMetrics.TryGetValue(routeId, out var metrics))
            {
                metrics = new RouteMetrics { RouteId = routeId, ClusterId = entry.ClusterId };
                routeMetrics[routeId] = metrics;
            }

            metrics.TotalRequests++;

            if (entry.StatusCode >= 400)
                metrics.ErrorCount++;

            if (entry.ElapsedMs.HasValue)
            {
                metrics.LatencySum += entry.ElapsedMs.Value;
                metrics.LatencyCount++;
                if (entry.ElapsedMs.Value > metrics.MaxLatency)
                    metrics.MaxLatency = entry.ElapsedMs.Value;
            }

            if (entry.Timestamp > metrics.LastSeen)
                metrics.LastSeen = entry.Timestamp;
        }

        var broadcasts = routeMetrics.Values
            .Where(m => m.TotalRequests > 0)
            .Select(m =>
            {
                double errorRate = m.TotalRequests > 0
                    ? Math.Round((double)m.ErrorCount / m.TotalRequests * 100, 1)
                    : 0;
                double avgLatency = m.LatencyCount > 0
                    ? Math.Round(m.LatencySum / m.LatencyCount, 1)
                    : 0;
                double rps = Math.Round(m.TotalRequests / _windowDuration.TotalSeconds, 2);
                return new RealTimeTrafficData
                {
                    RouteId = m.RouteId,
                    ClusterId = m.ClusterId,
                    RequestsPerSecond = rps,
                    RequestsPerMinute = m.TotalRequests,
                    ErrorRate = errorRate,
                    AvgLatencyMs = avgLatency,
                    P99LatencyMs = Math.Round(m.MaxLatency, 1),
                    BytesIn = 0,
                    BytesOut = 0,
                    ActiveConnections = 0,
                    Status = errorRate > 10 ? "degraded" : "normal",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
            })
            .ToList();

        if (broadcasts.Count > 0)
        {
            await _hubContext.Clients.Group("traffic").SendCoreAsync(
                "TrafficUpdate",
                new object[] { broadcasts },
                ct);
        }
    }

    private class RouteMetrics
    {
        public string RouteId { get; init; } = "";
        public string? ClusterId { get; init; }
        public int TotalRequests { get; set; }
        public int ErrorCount { get; set; }
        public double LatencySum { get; set; }
        public int LatencyCount { get; set; }
        public double MaxLatency { get; set; }
        public DateTime LastSeen { get; set; } = DateTime.MinValue;
    }
}
