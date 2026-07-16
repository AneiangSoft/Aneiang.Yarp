using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Models;

/// <summary>
/// Destination response with health status.
/// </summary>
public class DashboardDestinationResponse
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("health")]
    public string? Health { get; set; }

    [JsonPropertyName("host")]
    public string? Host { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }

    [JsonPropertyName("activeHealth")]
    public string ActiveHealth { get; set; } = string.Empty;

    [JsonPropertyName("passiveHealth")]
    public string PassiveHealth { get; set; } = string.Empty;
}
