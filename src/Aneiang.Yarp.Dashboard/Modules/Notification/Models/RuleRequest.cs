using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.Notification.Models;

/// <summary>
/// Request DTO for creating or updating a notification rule.
/// </summary>
public class RuleRequest
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("eventTypes")]
    public List<string>? EventTypes { get; set; }

    [JsonPropertyName("channelIds")]
    public List<string> ChannelIds { get; set; } = [];

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("cooldownSeconds")]
    public int CooldownSeconds { get; set; } = 300;

    [JsonPropertyName("recordToHistory")]
    public bool RecordToHistory { get; set; } = true;

    [JsonPropertyName("targetRouteIds")]
    public List<string>? TargetRouteIds { get; set; }

    [JsonPropertyName("targetClusterIds")]
    public List<string>? TargetClusterIds { get; set; }
}
