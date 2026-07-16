using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.Notification.Services;

/// <summary>
/// Payload for DingTalk (钉钉) webhook notifications.
/// </summary>
internal class DingTalkPayload
{
    [JsonPropertyName("msgtype")]
    public string MsgType { get; set; } = "text";
    [JsonPropertyName("markdown")]
    public DingTalkMarkdown? Markdown { get; set; }
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}
