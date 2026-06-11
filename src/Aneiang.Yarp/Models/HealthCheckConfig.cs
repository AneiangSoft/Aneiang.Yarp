namespace Aneiang.Yarp.Models;

/// <summary>
/// Health check configuration for clusters.
/// </summary>
public class HealthCheckConfig
{
    /// <summary>Enable active health checking.</summary>
    public bool Active { get; set; }

    /// <summary>Active health check endpoint URL path.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Interval between health checks.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Health check timeout.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Enable passive health checking. Default: false.</summary>
    public bool Passive { get; set; }

    /// <summary>Passive health check policy name. Default: "ConsecutiveFailures".</summary>
    public string? PassivePolicy { get; set; } = "ConsecutiveFailures";

    /// <summary>Reactivation period for passive health checks. Default: 30 seconds.</summary>
    public TimeSpan PassiveReactivationPeriod { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Available destinations policy: "Any", "AtLeastOne", "Majority".</summary>
    public string? AvailableDestinationsPolicy { get; set; }
}
