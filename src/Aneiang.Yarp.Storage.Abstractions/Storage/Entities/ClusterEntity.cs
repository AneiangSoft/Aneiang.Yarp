namespace Aneiang.Yarp.Storage;

/// <summary>YARP Cluster entity for database storage.</summary>
public class ClusterEntity
{
    public string ClusterUid { get; set; } = Guid.NewGuid().ToString("N");
    public string ClusterId { get; set; } = string.Empty;
    public string ClusterKey
    {
        get => ClusterId;
        set => ClusterId = value;
    }
    public string? DisplayName { get; set; }
    public string? LoadBalancingPolicy { get; set; }
    public string? HealthCheckConfig { get; set; } // JSON
    public string? CircuitBreakerConfig { get; set; } // JSON
    public string Source { get; set; } = "dynamic";
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public DateTime? LastHeartbeat { get; set; }

    /// <summary>Full native YARP ClusterConfig serialized as PascalCase JSON. Carries all advanced properties.</summary>
    public string? ConfigJson { get; set; }
}
