namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Dtos;

/// <summary>Statistics data DTO for dashboard overview.</summary>
public class StatsData
{
    public bool HasData { get; set; }
    public int TotalRequests { get; set; }
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
    public double SuccessRate { get; set; }
    public double ErrorRate { get; set; }
    public double AvgLatency { get; set; }
    public double P50 { get; set; }
    public double P90 { get; set; }
    public double P99 { get; set; }
    public int RequestsPerMin { get; set; }
    public List<StatusCodeItem> StatusCodes { get; set; } = new();
    public List<TopItem> TopRoutes { get; set; } = new();
    public List<TopItem> TopClusters { get; set; } = new();
    public DateTime ComputedAt { get; set; }
}

/// <summary>HTTP status code distribution item.</summary>
public class StatusCodeItem
{
    public int Code { get; set; }
    public int Count { get; set; }
}

/// <summary>Top-N item (route or cluster).</summary>
public class TopItem
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}
