namespace Aneiang.Yarp.Models;

/// <summary>Update cluster request.</summary>
public class UpdateClusterRequest
{
    /// <summary>Destination addresses (load balancing targets).</summary>
    public Dictionary<string, string>? Destinations { get; set; }

    /// <summary>Load balancing policy.</summary>
    public string? LoadBalancingPolicy { get; set; }

    /// <summary>Health check configuration.</summary>
    public ClusterHealthCheckConfig? HealthCheck { get; set; }
}
