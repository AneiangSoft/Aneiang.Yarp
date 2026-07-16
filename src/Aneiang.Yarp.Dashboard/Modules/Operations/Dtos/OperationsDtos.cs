namespace Aneiang.Yarp.Dashboard.Modules.Operations.Dtos;

/// <summary>Alert summary for the top alert bar.</summary>
public class AlertSummaryData
{
    public int UnhealthyCount { get; set; }
    public int UnhealthyTotal { get; set; }
    public int CircuitBreakerCount { get; set; }
    public int RecentErrors { get; set; }
    public int UnhandledEvents { get; set; }
    public DateTime LastUpdated { get; set; }
}

/// <summary>Traffic time-series data for charts.</summary>
public class TrafficData
{
    public List<string> Labels { get; set; } = new();
    public List<int> Qps { get; set; } = new();
    public List<int> Errors { get; set; } = new();
    public int CurrentQps { get; set; }
    public int TimeRange { get; set; }
}

/// <summary>Error route breakdown item.</summary>
public class ErrorRouteItem
{
    public string RouteId { get; set; } = string.Empty;
    public int ErrorCount { get; set; }
    public int TotalCount { get; set; }
    public int RecentErrors { get; set; }
    public double ErrorRate => TotalCount > 0 ? Math.Round((double)ErrorCount / TotalCount * 100, 1) : 0;
}

/// <summary>Slow cluster latency breakdown item.</summary>
public class SlowClusterItem
{
    public string ClusterId { get; set; } = string.Empty;
    public double AvgLatency { get; set; }
    public double P50Latency { get; set; }
    public double P90Latency { get; set; }
    public double P99Latency { get; set; }
    public int RequestCount { get; set; }
}

/// <summary>Top issues response combining error routes and slow clusters.</summary>
public class TopIssuesData
{
    public List<ErrorRouteItem> ErrorRoutes { get; set; } = new();
    public List<SlowClusterItem> SlowClusters { get; set; } = new();
}

/// <summary>Health summary for cluster destinations.</summary>
public class HealthSummaryData
{
    public double HealthScore { get; set; }
    public int TotalClusters { get; set; }
    public int TotalDestinations { get; set; }
    public int HealthyCount { get; set; }
    public int UnhealthyCount { get; set; }
    public int UnknownCount { get; set; }
    public string Status { get; set; } = "Healthy";
}

/// <summary>System snapshot for export.</summary>
public class SystemSnapshot
{
    public DateTime ExportedAt { get; set; }
    public int ClusterCount { get; set; }
    public int RouteCount { get; set; }
    public List<ClusterSnapshot> Clusters { get; set; } = new();
    public List<RouteSnapshot> Routes { get; set; } = new();
}

/// <summary>Cluster snapshot item.</summary>
public class ClusterSnapshot
{
    public string Id { get; set; } = string.Empty;
    public int DestinationCount { get; set; }
}

/// <summary>Route snapshot item.</summary>
public class RouteSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string? ClusterId { get; set; }
}
