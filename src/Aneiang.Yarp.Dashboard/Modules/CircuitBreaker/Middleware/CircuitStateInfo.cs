namespace Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;

/// <summary>
/// Read-only snapshot of circuit state for API consumption.
/// </summary>
public class CircuitStateInfo
{
    public string Key { get; set; } = string.Empty;
    public string ClusterUid { get; set; } = string.Empty;
    public string ClusterKeySnapshot { get; set; } = string.Empty;
    public string ClusterName { get; set; } = string.Empty;
    public string DestinationUid { get; set; } = "any";
    public string DestinationKeySnapshot { get; set; } = "any";
    public string Status { get; set; } = "Closed";
    public int ConsecutiveFailures { get; set; }
    public int FailureThreshold { get; set; }
    public TimeSpan RecoveryTimeout { get; set; }
    public int RecoveryTimeoutSeconds { get; set; }
    public int HalfOpenRequests { get; set; }
    public int MaxHalfOpenAttempts { get; set; }
    public DateTime? OpenedAt { get; set; }
    public DateTime LastAccessedAt { get; set; }
}
