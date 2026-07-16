using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Models;

/// <summary>
/// Health check configuration.
/// </summary>
public class HealthCheckInfo
{
    [JsonPropertyName("active")]
    public ActiveHealthCheckInfo? Active { get; set; }

    [JsonPropertyName("passive")]
    public PassiveHealthCheckInfo? Passive { get; set; }

    [JsonPropertyName("availableDestinationsPolicy")]
    public string? AvailableDestinationsPolicy { get; set; }
}
