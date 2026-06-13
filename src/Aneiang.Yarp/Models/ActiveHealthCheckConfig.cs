namespace Aneiang.Yarp.Models;

/// <summary>Active health check configuration.</summary>
public class ActiveHealthCheckConfig
{
    /// <summary>Enable active health checking.</summary>
    public bool Enabled { get; set; }

    /// <summary>Interval between health checks.</summary>
    public string? Interval { get; set; }

    /// <summary>Health check timeout.</summary>
    public string? Timeout { get; set; }

    /// <summary>Health check policy name.</summary>
    public string? Policy { get; set; }

    /// <summary>Health check endpoint path.</summary>
    public string? Path { get; set; }
}
