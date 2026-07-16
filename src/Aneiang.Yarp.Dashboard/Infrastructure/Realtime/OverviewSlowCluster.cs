using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Realtime;

public class OverviewSlowCluster
{
    [JsonPropertyName("clusterId")]
    public string ClusterId { get; set; } = string.Empty;

    [JsonPropertyName("avgLatency")]
    public double AvgLatency { get; set; }

    [JsonPropertyName("p99Latency")]
    public double P99Latency { get; set; }
}
