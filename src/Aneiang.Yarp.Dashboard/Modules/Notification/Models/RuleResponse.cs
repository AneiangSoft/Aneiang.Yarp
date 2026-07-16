using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.Notification.Models;

/// <summary>
/// Response DTO for a notification rule with full details.
/// </summary>
public class RuleResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("eventTypes")]
    public List<string> EventTypes { get; set; } = [];

    [JsonPropertyName("channelIds")]
    public List<string> ChannelIds { get; set; } = [];

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("cooldownSeconds")]
    public int CooldownSeconds { get; set; }

    [JsonPropertyName("recordToHistory")]
    public bool RecordToHistory { get; set; }

    [JsonPropertyName("targetRouteIds")]
    public List<string>? TargetRouteIds { get; set; }

    [JsonPropertyName("targetClusterIds")]
    public List<string>? TargetClusterIds { get; set; }

    [JsonPropertyName("channels")]
    public List<ChannelResponse>? ChannelDetails { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}
