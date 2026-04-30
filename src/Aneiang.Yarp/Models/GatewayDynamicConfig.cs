namespace Aneiang.Yarp.Models;

/// <summary>
/// Dynamic route configuration with metadata for persistence.
/// </summary>
public class DynamicRouteConfig
{
    /// <summary>Route ID (unique identifier).</summary>
    public string RouteId { get; set; } = string.Empty;

    /// <summary>Cluster ID this route belongs to.</summary>
    public string ClusterId { get; set; } = string.Empty;

    /// <summary>Route match path pattern.</summary>
    public string MatchPath { get; set; } = string.Empty;

    /// <summary>Route order (lower = higher priority).</summary>
    public int Order { get; set; } = 50;

    /// <summary>Route transforms (path rewriting, header manipulation, etc.).</summary>
    public List<Dictionary<string, string>>? Transforms { get; set; }

    /// <summary>Configuration source: "config" | "dynamic" | "auto-register".</summary>
    public string Source { get; set; } = "dynamic";

    /// <summary>When this route was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Who created this route (user name or "auto").</summary>
    public string? CreatedBy { get; set; }
}

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
}

/// <summary>
/// Health check configuration for clusters.
/// </summary>
public class HealthCheckConfig
{
    /// <summary>Enable active health checking.</summary>
    public bool Active { get; set; }

    /// <summary>Health check endpoint URL.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Interval between health checks.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Health check timeout.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Container for all dynamic gateway configurations.
/// </summary>
public class GatewayDynamicConfig
{
    /// <summary>Schema version for future migrations.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Last modified timestamp.</summary>
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    /// <summary>Dynamic routes (manually added or auto-registered).</summary>
    public List<DynamicRouteConfig> Routes { get; set; } = new();

    /// <summary>Dynamic clusters (manually added or auto-registered).</summary>
    public List<DynamicClusterConfig> Clusters { get; set; } = new();
}
