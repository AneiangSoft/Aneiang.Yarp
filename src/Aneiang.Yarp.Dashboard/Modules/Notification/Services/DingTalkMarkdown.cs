using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.Notification.Services;

/// <summary>
/// Markdown content format for DingTalk (钉钉) webhook messages.
/// </summary>
internal class DingTalkMarkdown
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}
