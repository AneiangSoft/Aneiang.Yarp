namespace Aneiang.Yarp.Dashboard.Modules.Notification.Services;

/// <summary>
/// Payload for generic webhook notifications.
/// </summary>
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
