namespace Aneiang.Yarp.Dashboard.Modules.Webhook.Services;

/// <summary>
/// Abstraction for platform-specific webhook delivery.
/// Each platform (DingTalk, Feishu, generic, etc.) implements this interface.
/// The pipeline auto-selects the correct provider by matching against <see cref="SupportedHosts"/>.
/// </summary>
public interface IWebhookProvider
{
    /// <summary>
    /// Host patterns this provider handles (e.g. "oapi.dingtalk.com").
    /// Compared case-insensitively against the webhook URL host.
    /// </summary>
    string[] SupportedHosts { get; }

    /// <summary>
    /// Platform identifier used for secret lookup and UI display (e.g. "dingtalk", "feishu", "wecom").
    /// </summary>
    string PlatformName { get; }

    /// <summary>
    /// Build the HTTP request (URL, headers, body) for this platform.
    /// </summary>
    /// <param name="url">Original webhook URL.</param>
    /// <param name="payload">Standard config-change payload.</param>
    /// <param name="secret">Optional signing secret.</param>
    /// <returns>Prepared request details; Url may include query parameters for signing.</returns>
    WebhookRequest BuildRequest(string url, WebhookPayload payload, string? secret);
}

/// <summary>
/// Result of <see cref="IWebhookProvider.BuildRequest"/>.
/// </summary>
public class WebhookRequest
{
    /// <summary>Request URL (may include query parameters for signature).</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>HTTP method. Default: POST.</summary>
    public string Method { get; set; } = HttpMethod.Post.Method;

    /// <summary>Additional headers to include.</summary>
    public Dictionary<string, string> Headers { get; } = new();

    /// <summary>Serialized request body; set to null for platforms that encode data in the URL.</summary>
    public string? Body { get; set; }

    /// <summary>Content type. Default: application/json.</summary>
    public string ContentType { get; set; } = "application/json";
}

/// <summary>
/// Standard config-change payload sent to all webhook providers.
/// </summary>
public class WebhookPayload
{
    /// <summary>Machine-readable event type key (e.g. "AddRoute", "UpdateCluster").</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Human-readable event label (e.g. "➕ 路由新增", "✏️ 集群更新").</summary>
    public string EventLabel { get; set; } = string.Empty;

    /// <summary>Target object of the change (route name, cluster ID, etc.).</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Who performed the operation.</summary>
    public string? Operator { get; set; }

    /// <summary>UTC timestamp of the event.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Event-specific details object.</summary>
    public object? Details { get; set; }

    /// <summary>Gateway display name (from DashboardOptions.Title).</summary>
    public string? GatewayName { get; set; }
}
