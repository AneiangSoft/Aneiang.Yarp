namespace Aneiang.Yarp.Storage;

/// <summary>A single webhook endpoint (URL + optional secret) for a notification platform.</summary>
public sealed class WebhookEndpointConfig
{
    public string Url { get; set; } = string.Empty;

    /// <summary>Optional secret. For DingTalk this is the robot's sign secret (HMAC-SHA256).</summary>
    public string? Secret { get; set; }
}

/// <summary>Aggregated webhook notification settings across all platforms.</summary>
public sealed class WebhookSettingsData
{
    /// <summary>
    /// Platform key (e.g. "dingtalk", "generic") -> configured endpoints.
    /// Unknown platform keys are preserved so custom integrations survive round-trips.
    /// </summary>
    public Dictionary<string, List<WebhookEndpointConfig>> Platforms { get; set; } = new();

    /// <summary>Config-change event types that trigger a webhook notification (e.g. "AddRoute").</summary>
    public List<string> EnabledEvents { get; set; } = new();
}

/// <summary>
/// Persistence abstraction for webhook notification settings
/// (platform endpoints + subscribed config-change events).
/// </summary>
public interface IWebhookSettingsRepository
{
    /// <summary>Load webhook settings. Never returns null; missing settings yield empty collections.</summary>
    Task<WebhookSettingsData> LoadAsync(CancellationToken ct = default);

    /// <summary>Persist the full webhook settings document.</summary>
    Task SaveAsync(WebhookSettingsData settings, CancellationToken ct = default);
}
