using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Models;

/// <summary>
/// Dynamic cluster configuration: holds the complete native YARP <see cref="ClusterConfig"/>
/// plus extension metadata that YARP itself does not track (UID, source, heartbeat, circuit breaker).
/// The native config is the single source of truth for all YARP fields.
/// </summary>
public sealed class DynamicClusterConfig
{
    /// <summary>
    /// Complete native YARP <see cref="ClusterConfig"/>. Carries all fields including advanced
    /// properties (SessionAffinity, HttpClient, HttpRequest, per-destination metadata, etc.).
    /// </summary>
    public ClusterConfig Config { get; set; } = new() { ClusterId = string.Empty };

    /// <summary>Internal immutable cluster UID.</summary>
    public string ClusterUid { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Display name for UI. Defaults to ClusterKey when empty.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Health check configuration (domain model, separate from native YARP HealthCheck).</summary>
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
