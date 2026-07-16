using System.Text.Json.Serialization;
using Aneiang.Yarp.Storage;

namespace Aneiang.Yarp.Dashboard.Modules.Notification.Models;

/// <summary>
/// Request DTO for saving all notification settings (channels, rules, global config).
/// </summary>
public class SaveNotificationSettingsRequest
{
    [JsonPropertyName("channels")]
    public List<ChannelRequest>? Channels { get; set; }

    [JsonPropertyName("rules")]
    public List<RuleRequest>? Rules { get; set; }

    [JsonPropertyName("globalSettings")]
    public GlobalSettingsRequest? GlobalSettings { get; set; }
}
