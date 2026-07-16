using System.Text.Json.Serialization;
using Aneiang.Yarp.Storage;

namespace Aneiang.Yarp.Dashboard.Modules.Notification.Models;

/// <summary>
/// Summary response DTO for notification overview (counts, last event, channel stats).
/// </summary>
public class NotificationSummaryResponse
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("unread")]
    public int Unread { get; set; }

    [JsonPropertyName("byEventType")]
    public Dictionary<string, int> ByEventType { get; set; } = new();

    [JsonPropertyName("lastEvent")]
    public NotificationHistory? LastEvent { get; set; }

    [JsonPropertyName("channelsConfigured")]
    public int ChannelsConfigured { get; set; }

    [JsonPropertyName("rulesActive")]
    public int RulesActive { get; set; }
}
