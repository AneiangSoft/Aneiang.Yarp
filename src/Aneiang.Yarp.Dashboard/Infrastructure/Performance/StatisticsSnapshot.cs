namespace Aneiang.Yarp.Dashboard.Infrastructure.Performance;

/// <summary>
/// Statistics snapshot with pre-computed aggregations.
/// </summary>
public readonly struct StatisticsSnapshot
{
    public long TotalRequests { get; init; }
    public long SuccessCount { get; init; }
    public long ErrorCount { get; init; }
    public double SuccessRate { get; init; }
    public double ErrorRate { get; init; }
    public long AvgLatencyMicros { get; init; }
    public KeyValuePair<int, long>[] StatusCodes { get; init; }
    public KeyValuePair<int, long>[] TopRoutes { get; init; }
    public KeyValuePair<int, long>[] TopClusters { get; init; }
    public DateTime ComputedAt { get; init; }
}
