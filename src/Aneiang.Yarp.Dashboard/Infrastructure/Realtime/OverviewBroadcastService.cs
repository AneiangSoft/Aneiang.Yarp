using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Realtime;

/// <summary>
/// Background service that pushes the shared <see cref="OverviewSnapshot"/>
/// (built by <see cref="IOverviewSnapshotProvider"/>) to all connected Overview
/// SignalR clients every 5 seconds.
/// </summary>
internal sealed class OverviewBroadcastService : BackgroundService
{
    private readonly IHubContext<OverviewHub> _hubContext;
    private readonly IOverviewSnapshotProvider _snapshotProvider;
    private readonly ILogger<OverviewBroadcastService> _logger;

    private static readonly TimeSpan BroadcastInterval = TimeSpan.FromSeconds(5);

    public OverviewBroadcastService(
        IHubContext<OverviewHub> hubContext,
        IOverviewSnapshotProvider snapshotProvider,
        ILogger<OverviewBroadcastService> logger)
    {
        _hubContext = hubContext;
        _snapshotProvider = snapshotProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug(
            "OverviewBroadcastService started - broadcasting every {Interval}s",
            BroadcastInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(BroadcastInterval, stoppingToken);
                var snapshot = await _snapshotProvider.GetSnapshotAsync(stoppingToken);
                await _hubContext.Clients.Group("overview").SendCoreAsync(
                    "OverviewUpdate",
                    new object[] { snapshot },
                    stoppingToken);
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
}

// ─── Data models ─────────────────────────────────────────────────────────────

/// <summary>
/// Aggregated overview snapshot pushed to all connected Overview clients.
/// </summary>
public class OverviewSnapshot
{
    [JsonPropertyName("clusterCount")]
    public int ClusterCount { get; set; }

    [JsonPropertyName("routeCount")]
    public int RouteCount { get; set; }

    [JsonPropertyName("healthyCount")]
    public int HealthyCount { get; set; }

    [JsonPropertyName("unknownCount")]
    public int UnknownCount { get; set; }

    [JsonPropertyName("unhealthyCount")]
    public int UnhealthyCount { get; set; }

    [JsonPropertyName("currentQps")]
    public double CurrentQps { get; set; }

    [JsonPropertyName("cpuUsage")]
    public double CpuUsage { get; set; }

    [JsonPropertyName("memoryMb")]
    public long MemoryMb { get; set; }

    [JsonPropertyName("gcCount")]
    public int GcCount { get; set; }

    [JsonPropertyName("threadCount")]
    public int ThreadCount { get; set; }

    [JsonPropertyName("topErrorRoutes")]
    public List<OverviewErrorRoute> TopErrorRoutes { get; set; } = new();

    [JsonPropertyName("topSlowClusters")]
    public List<OverviewSlowCluster> TopSlowClusters { get; set; } = new();

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }
}

/// <summary>
/// A single error-route entry in the overview snapshot.
/// </summary>
public class OverviewErrorRoute
{
    [JsonPropertyName("routeId")]
    public string RouteId { get; set; } = string.Empty;

    [JsonPropertyName("errorCount")]
    public int ErrorCount { get; set; }

    [JsonPropertyName("errorRate")]
    public double ErrorRate { get; set; }
}

/// <summary>
/// A single slow-cluster entry in the overview snapshot.
/// </summary>
public class OverviewSlowCluster
{
    [JsonPropertyName("clusterId")]
    public string ClusterId { get; set; } = string.Empty;

    [JsonPropertyName("avgLatency")]
    public double AvgLatency { get; set; }

    [JsonPropertyName("p99Latency")]
    public double P99Latency { get; set; }
}
