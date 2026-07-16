using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Models;

/// <summary>
/// Passive health check configuration.
/// </summary>
public class PassiveHealthCheckInfo
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("policy")]
    public string? Policy { get; set; }

    [JsonPropertyName("reactivationPeriod")]
    public string? ReactivationPeriod { get; set; }
}
