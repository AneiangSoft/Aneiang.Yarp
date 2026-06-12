namespace Aneiang.Yarp.Models;

/// <summary>
/// Dynamic cluster configuration with metadata for persistence.
/// </summary>
public class DynamicClusterConfig
{
    /// <summary>Cluster ID (unique identifier).</summary>
    public string ClusterId { get; set; } = string.Empty;

    /// <summary>Destination addresses (load balancing targets).</summary>
    public Dictionary<string, string> Destinations { get; set; } = new();

    /// <summary>Load balancing policy: RoundRobin, PowerOfTwoChoices, Random, LeastRequests.</summary>
    public string? LoadBalancingPolicy { get; set; }

    /// <summary>Health check configuration.</summary>
    public HealthCheckConfig? HealthCheck { get; set; }

    /// <summary>Configuration source: "config" | "dynamic" | "auto-register".</summary>
    public string Source { get; set; } = "dynamic";

    /// <summary>When this cluster was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Who created this cluster (user name or "auto").</summary>
    public string? CreatedBy { get; set; }

    /// <summary>Last heartbeat time from registered services. Used for stale detection.</summary>
    public DateTime? LastHeartbeat { get; set; }

    /// <summary>Circuit breaker configuration at cluster level.</summary>
    public CircuitBreakerConfig? CircuitBreaker { get; set; }
}
