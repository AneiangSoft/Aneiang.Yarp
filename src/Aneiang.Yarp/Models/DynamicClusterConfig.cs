using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Models;

public sealed class DynamicClusterConfig
{
    public ClusterConfig Config { get; set; } = new() { ClusterId = string.Empty };

    public string ClusterUid { get; set; } = Guid.NewGuid().ToString("N");

    public string? DisplayName { get; set; }

    public HealthCheckConfig? HealthCheck { get; set; }

    public string Source { get; set; } = "dynamic";

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string? CreatedBy { get; set; }

    public DateTime? LastHeartbeat { get; set; }

    public CircuitBreakerConfig? CircuitBreaker { get; set; }
}
