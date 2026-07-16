using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Realtime;

/// <summary>
/// Real-time traffic data for a single route/cluster edge.
/// Broadcast to SignalR clients for live topology animation.
/// </summary>
public class RealTimeTrafficData
{
    [JsonPropertyName("routeId")]
    public string RouteId { get; set; } = string.Empty;

    [JsonPropertyName("clusterId")]
    public string? ClusterId { get; set; }

    [JsonPropertyName("requestsPerSecond")]
    public double RequestsPerSecond { get; set; }

    [JsonPropertyName("requestsPerMinute")]
    public int RequestsPerMinute { get; set; }

    [JsonPropertyName("errorRate")]
    public double ErrorRate { get; set; }

    [JsonPropertyName("avgLatencyMs")]
    public double AvgLatencyMs { get; set; }

    [JsonPropertyName("p99LatencyMs")]
    public double P99LatencyMs { get; set; }

    [JsonPropertyName("bytesIn")]
    public long BytesIn { get; set; }

    [JsonPropertyName("bytesOut")]
    public long BytesOut { get; set; }

    [JsonPropertyName("activeConnections")]
    public int ActiveConnections { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "normal";

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }
}
