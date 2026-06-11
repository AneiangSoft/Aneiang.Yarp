namespace Aneiang.Yarp.Storage;

/// <summary>YARP Cluster entity for database storage.</summary>
public class ClusterEntity
{
    public string ClusterId { get; set; } = string.Empty;
    public string? LoadBalancingPolicy { get; set; }
    public string? HealthCheckConfig { get; set; } // JSON
    public string Source { get; set; } = "dynamic";
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastHeartbeat { get; set; }
}
