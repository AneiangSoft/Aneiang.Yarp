namespace Aneiang.Yarp.Models;

/// <summary>Health check configuration matching YARP's structure.</summary>
public class ClusterHealthCheckConfig
{
    /// <summary>Active health check configuration.</summary>
    public ActiveHealthCheckConfig? Active { get; set; }

    /// <summary>Passive health check configuration.</summary>
    public PassiveHealthCheckConfig? Passive { get; set; }

    /// <summary>Available destinations policy.</summary>
    public string? AvailableDestinationsPolicy { get; set; }
}
