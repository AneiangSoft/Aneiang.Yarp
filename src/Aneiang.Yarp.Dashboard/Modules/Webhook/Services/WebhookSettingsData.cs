namespace Aneiang.Yarp.Dashboard.Modules.Webhook.Services;

/// <summary>Represents a single webhook endpoint with URL and optional secret.</summary>
public class WebhookEndpoint
{
    /// <summary>Webhook URL.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Optional signing secret for this endpoint.</summary>
    public string? Secret { get; set; }
}

/// <summary>Data model for persisted webhook settings.</summary>
public class WebhookSettingsData
{
    /// <summary>DingTalk webhook endpoints (each with URL and optional secret).</summary>
    public List<WebhookEndpoint> DingTalkEndpoints { get; set; } = [];

    /// <summary>Generic webhook endpoints (each with URL and optional secret).</summary>
    public List<WebhookEndpoint> GenericEndpoints { get; set; } = [];

    /// <summary>
    /// List of enabled webhook event types. When empty or null, all events are enabled.
    /// </summary>
    public List<string>? EnabledEvents { get; set; }

    /// <summary>HTTP request timeout in seconds. Default 10.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Number of retry attempts on failure. Default 1.</summary>
    public int RetryCount { get; set; } = 1;
}
