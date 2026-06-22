namespace Aneiang.Yarp.Storage;

/// <summary>
/// Repository interface for notification settings and history.
/// </summary>
public interface INotificationRepository
{
    // ─── Settings ──────────────────────────────────────────────────────────────

    /// <summary>Load all notification settings (channels, rules, global settings).</summary>
    Task<NotificationSettingsEntity?> LoadSettingsAsync(CancellationToken ct = default);

    /// <summary>Save notification settings.</summary>
    Task SaveSettingsAsync(NotificationSettingsEntity settings, CancellationToken ct = default);

    // ─── Channels ─────────────────────────────────────────────────────────────

    /// <summary>Get all notification channels.</summary>
    Task<List<NotificationChannel>> GetChannelsAsync(CancellationToken ct = default);

    /// <summary>Get a channel by ID.</summary>
    Task<NotificationChannel?> GetChannelAsync(string channelId, CancellationToken ct = default);

    /// <summary>Add or update a channel.</summary>
    Task SaveChannelAsync(NotificationChannel channel, CancellationToken ct = default);

    /// <summary>Delete a channel by ID.</summary>
    Task DeleteChannelAsync(string channelId, CancellationToken ct = default);

    // ─── Rules ────────────────────────────────────────────────────────────────

    /// <summary>Get all notification rules.</summary>
    Task<List<NotificationRule>> GetRulesAsync(CancellationToken ct = default);

    /// <summary>Get a rule by ID.</summary>
    Task<NotificationRule?> GetRuleAsync(string ruleId, CancellationToken ct = default);

    /// <summary>Add or update a rule.</summary>
    Task SaveRuleAsync(NotificationRule rule, CancellationToken ct = default);

    /// <summary>Delete a rule by ID.</summary>
    Task DeleteRuleAsync(string ruleId, CancellationToken ct = default);

    // ─── Global Settings ──────────────────────────────────────────────────────

    /// <summary>Get global notification settings.</summary>
    Task<NotificationGlobalSettings> GetGlobalSettingsAsync(CancellationToken ct = default);

    /// <summary>Save global notification settings.</summary>
    Task SaveGlobalSettingsAsync(NotificationGlobalSettings settings, CancellationToken ct = default);

    // ─── History ──────────────────────────────────────────────────────────────

    /// <summary>Record a notification event to history.</summary>
    Task RecordNotificationAsync(NotificationHistory record, CancellationToken ct = default);

    /// <summary>Get notification history with pagination.</summary>
    Task<(List<NotificationHistory> Records, int Total)> GetHistoryAsync(
        int page = 1,
        int pageSize = 100,
        string? eventType = null,
        string? severity = null,
        string? dateStart = null,
        string? dateEnd = null,
        CancellationToken ct = default);

    /// <summary>Clear notification history.</summary>
    Task ClearHistoryAsync(CancellationToken ct = default);
}

/// <summary>
/// Global notification settings.
/// </summary>
public class NotificationGlobalSettings
{
    public bool Enabled { get; set; } = true;
    public int MaxHistoryRecords { get; set; } = 500;
    public int DefaultTimeoutSeconds { get; set; } = 10;
    public int DefaultRetryCount { get; set; } = 1;
    /// <summary>Notification message locale: "zh-CN" or "en-US". Defaults to "zh-CN".</summary>
    public string Locale { get; set; } = "zh-CN";
}
