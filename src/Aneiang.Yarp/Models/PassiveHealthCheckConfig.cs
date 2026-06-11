namespace Aneiang.Yarp.Models;

/// <summary>Passive health check configuration.</summary>
public class PassiveHealthCheckConfig
{
    /// <summary>Enable passive health checking.</summary>
    public bool Enabled { get; set; }

    /// <summary>Passive health check policy name.</summary>
    public string? Policy { get; set; }

    /// <summary>Reactivation period for passive health checks.</summary>
    public string? ReactivationPeriod { get; set; }
}
