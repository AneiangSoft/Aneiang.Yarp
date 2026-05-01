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

    /// <summary>Load balancing policy: RoundRobin, PowerOfTwoChoices, Random, LeastRequests, FirstAlone.</summary>
    public string? LoadBalancingPolicy { get; set; } = "RoundRobin";

    /// <summary>Health check configuration.</summary>
    public ClusterHealthCheckConfig? HealthCheck { get; set; }
}

/// <summary>Update cluster request.</summary>
public class UpdateClusterRequest
{
    /// <summary>Destination addresses (load balancing targets).</summary>
    public Dictionary<string, string>? Destinations { get; set; }

    /// <summary>Load balancing policy: RoundRobin, PowerOfTwoChoices, Random, LeastRequests, FirstAlone.</summary>
    public string? LoadBalancingPolicy { get; set; }

    /// <summary>Health check configuration.</summary>
    public ClusterHealthCheckConfig? HealthCheck { get; set; }
}

/// <summary>Health check configuration matching YARP's structure.</summary>
public class ClusterHealthCheckConfig
{
    /// <summary>Active health check configuration.</summary>
    public ActiveHealthCheckConfig? Active { get; set; }

    /// <summary>Passive health check configuration.</summary>
    public PassiveHealthCheckConfig? Passive { get; set; }

    /// <summary>Available destinations policy: Any, AtLeastOne, Majority.</summary>
    public string? AvailableDestinationsPolicy { get; set; }
}

/// <summary>Active health check configuration.</summary>
public class ActiveHealthCheckConfig
{
    /// <summary>Enable active health checking.</summary>
    public bool Enabled { get; set; }

    /// <summary>Interval between health checks (e.g., "00:00:15" for 15 seconds).</summary>
    public string? Interval { get; set; }

    /// <summary>Health check timeout (e.g., "00:00:10" for 10 seconds).</summary>
    public string? Timeout { get; set; }

    /// <summary>Health check policy name.</summary>
    public string? Policy { get; set; }

    /// <summary>Health check endpoint path.</summary>
    public string? Path { get; set; }
}

/// <summary>Passive health check configuration.</summary>
public class PassiveHealthCheckConfig
{
    /// <summary>Enable passive health checking.</summary>
    public bool Enabled { get; set; }

    /// <summary>Passive health check policy name.</summary>
    public string? Policy { get; set; }

    /// <summary>Reactivation period (e.g., "00:00:30" for 30 seconds).</summary>
    public string? ReactivationPeriod { get; set; }
}
