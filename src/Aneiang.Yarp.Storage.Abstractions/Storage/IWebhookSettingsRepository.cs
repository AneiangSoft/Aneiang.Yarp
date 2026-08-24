namespace Aneiang.Yarp.Storage;

/// <summary>A single webhook endpoint (URL + optional secret) for a notification platform.</summary>
public sealed class WebhookEndpointConfig
{
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Optional secret. Platform-specific meaning:
    /// DingTalk - robot sign secret (HMAC-SHA256 URL signature);
    /// Feishu - custom-bot sign secret (HMAC-SHA256 body signature);
    /// Telegram - target chat_id (URL itself carries the bot token).
    /// </summary>
    public string? Secret { get; set; }
}

/// <summary>Aggregated webhook notification settings across all platforms.</summary>
public sealed class WebhookSettingsData
{
    /// <summary>
    /// Platform key (e.g. "dingtalk", "slack", "generic") -> configured endpoints.
    /// Supported keys: dingtalk / wechat / feishu / slack / discord / telegram / teams / generic.
    /// Unknown platform keys are preserved so custom integrations survive round-trips.
    /// </summary>
    public Dictionary<string, List<WebhookEndpointConfig>> Platforms { get; set; } = new();

    /// <summary>Config-change event types that trigger notifications globally (legacy, applies to platforms without per-platform subscription).</summary>
    public List<string> EnabledEvents { get; set; } = new();

    /// <summary>
    /// Per-platform event subscriptions (platform -> event keys, covering both config events
    /// and alert events). Absent platform entry falls back to <see cref="EnabledEvents"/>.
    /// </summary>
    public Dictionary<string, List<string>> PlatformEvents { get; set; } = new();

    /// <summary>Cooldown window in seconds for identical event+target pairs (anti alert-storm). Default 60.</summary>
    public int CooldownSeconds { get; set; } = 60;

    /// <summary>Retry attempts on failed delivery (exponential backoff 1s/4s/16s). Default 2.</summary>
    public int RetryCount { get; set; } = 2;

    /// <summary>HTTP timeout in seconds per delivery attempt. Default 10.</summary>
    public int TimeoutSeconds { get; set; } = 10;
}

/// <summary>One recorded webhook delivery attempt result.</summary>
public sealed class WebhookDeliveryRecord
{
    public long Id { get; set; }

    public string Platform { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string? Target { get; set; }

    public bool Success { get; set; }

    public int? HttpStatusCode { get; set; }

    public string? Error { get; set; }

    /// <summary>Total delivery duration in milliseconds (including retries).</summary>
    public int DurationMs { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>Persistence abstraction for webhook delivery history (bounded ring of recent records).</summary>
public interface IWebhookDeliveryRecordRepository
{
    /// <summary>Append a delivery record and prune the table down to <paramref name="maxRecords"/> rows.</summary>
    Task AddAsync(WebhookDeliveryRecord record, int maxRecords = 200, CancellationToken ct = default);

    /// <summary>Return the most recent delivery records, newest first.</summary>
    Task<List<WebhookDeliveryRecord>> GetRecentAsync(int limit = 200, CancellationToken ct = default);

    /// <summary>Delete all delivery records.</summary>
    Task ClearAsync(CancellationToken ct = default);
}

/// <summary>
/// Persistence abstraction for webhook notification settings
/// (platform endpoints + subscribed events + delivery tuning).
/// </summary>
public interface IWebhookSettingsRepository
{
    /// <summary>Load webhook settings. Never returns null; missing settings yield empty collections.</summary>
    Task<WebhookSettingsData> LoadAsync(CancellationToken ct = default);

    /// <summary>Persist the full webhook settings document.</summary>
    Task SaveAsync(WebhookSettingsData settings, CancellationToken ct = default);
}
