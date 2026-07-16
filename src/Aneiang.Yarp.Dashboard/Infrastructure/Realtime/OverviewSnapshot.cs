using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Realtime;

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
