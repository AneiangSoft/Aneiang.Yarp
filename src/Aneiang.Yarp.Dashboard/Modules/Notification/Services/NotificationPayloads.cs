using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.Notification.Services;

/// <summary>
/// Payload DTOs for notification channels.
/// Extracted from <see cref="NotificationService"/> for separation of concerns.
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

internal class DingTalkMarkdown
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

internal class GenericWebhookPayload
{
    public string EventType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? ClusterId { get; set; }
    public string? RouteId { get; set; }
    public string? ClientIp { get; set; }
    public Dictionary<string, string?> Metadata { get; set; } = new();
    public string GatewayName { get; set; } = string.Empty;
}
