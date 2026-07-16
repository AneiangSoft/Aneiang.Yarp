using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.Notification.Models;

/// <summary>
/// Response DTO for notification settings (channels, rules, global config).
/// </summary>
public class NotificationSettingsResponse
{
    [JsonPropertyName("channels")]
    public List<ChannelResponse> Channels { get; set; } = [];

    [JsonPropertyName("rules")]
    public List<RuleResponse> Rules { get; set; } = [];

    [JsonPropertyName("globalSettings")]
    public GlobalSettingsResponse GlobalSettings { get; set; } = new();
}
