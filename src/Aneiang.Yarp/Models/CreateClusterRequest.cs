using System.ComponentModel.DataAnnotations;

namespace Aneiang.Yarp.Models;

/// <summary>Create cluster request.</summary>
public class CreateClusterRequest
{
    /// <summary>Cluster ID (unique identifier).</summary>
    [Required(ErrorMessage = "Cluster ID is required")]
    [StringLength(200, MinimumLength = 1)]
    public string ClusterId { get; set; } = string.Empty;

    /// <summary>Destination addresses (load balancing targets).</summary>
    [Required(ErrorMessage = "At least one destination is required")]
    public Dictionary<string, string> Destinations { get; set; } = new();

    /// <summary>Load balancing policy.</summary>
    public string? LoadBalancingPolicy { get; set; } = "RoundRobin";

    /// <summary>Health check configuration.</summary>
    public ClusterHealthCheckConfig? HealthCheck { get; set; }
}
