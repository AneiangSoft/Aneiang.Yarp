namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Interface for webhook settings persistence via structured storage.
/// Supports preloading, loading, and saving of webhook configurations.
/// </summary>
public interface IWebhookSettingsPersistenceService
{
    /// <summary>Preload webhook settings into memory cache during startup.</summary>
    Task PreloadAsync(CancellationToken ct = default);

    /// <summary>Load webhook settings from store (synchronous, for startup/legacy contexts).</summary>
    WebhookSettingsData? Load();

    /// <summary>Load webhook settings from store asynchronously. Prefer this in async controller actions.</summary>
    Task<WebhookSettingsData?> LoadAsync(CancellationToken ct = default);

    /// <summary>Save webhook settings to store (synchronous, for legacy contexts).</summary>
    bool Save(WebhookSettingsData data);

    /// <summary>Save webhook settings to store asynchronously. Prefer this in async controller actions.</summary>
    Task<bool> SaveAsync(WebhookSettingsData data, CancellationToken ct = default);
}
