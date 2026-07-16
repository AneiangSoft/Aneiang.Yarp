namespace Aneiang.Yarp.Storage;

public interface INotificationRepository
{
    Task<NotificationSettingsEntity?> LoadSettingsAsync(CancellationToken ct = default);
    Task SaveSettingsAsync(NotificationSettingsEntity settings, CancellationToken ct = default);

    Task<List<NotificationChannel>> GetChannelsAsync(CancellationToken ct = default);
    Task<NotificationChannel?> GetChannelAsync(string channelId, CancellationToken ct = default);
    Task SaveChannelAsync(NotificationChannel channel, CancellationToken ct = default);
    Task DeleteChannelAsync(string channelId, CancellationToken ct = default);

    Task<List<NotificationRule>> GetRulesAsync(CancellationToken ct = default);
    Task<NotificationRule?> GetRuleAsync(string ruleId, CancellationToken ct = default);
    Task SaveRuleAsync(NotificationRule rule, CancellationToken ct = default);
    Task DeleteRuleAsync(string ruleId, CancellationToken ct = default);

    Task<NotificationGlobalSettings> GetGlobalSettingsAsync(CancellationToken ct = default);
    Task SaveGlobalSettingsAsync(NotificationGlobalSettings settings, CancellationToken ct = default);

    Task RecordNotificationAsync(NotificationHistory record, CancellationToken ct = default);

    Task<(List<NotificationHistory> Records, int Total)> GetHistoryAsync(
        int page = 1,
        int pageSize = 100,
        string? eventType = null,
        string? severity = null,
        string? dateStart = null,
        string? dateEnd = null,
        CancellationToken ct = default);

    Task ClearHistoryAsync(CancellationToken ct = default);
}
