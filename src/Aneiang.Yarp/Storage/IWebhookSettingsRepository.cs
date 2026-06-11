namespace Aneiang.Yarp.Storage;

/// <summary>
/// Webhook settings repository for webhook configuration persistence.
/// </summary>
public interface IWebhookSettingsRepository
{
    Task<WebhookSettingsEntity?> GetWebhookSettingsAsync(CancellationToken ct = default);
    Task SaveWebhookSettingsAsync(WebhookSettingsEntity settings, CancellationToken ct = default);
}
