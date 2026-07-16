namespace Aneiang.Yarp.Storage;

public class ProxyLogStatsResult
{
    public long TotalRequests { get; set; }
    public long SuccessCount { get; set; }
    public long ErrorCount { get; set; }
    public double AvgLatencyMs { get; set; }
    public double P50LatencyMs { get; set; }
    public double P90LatencyMs { get; set; }
    public double P99LatencyMs { get; set; }
    public int RequestsPerMinute { get; set; }
}
