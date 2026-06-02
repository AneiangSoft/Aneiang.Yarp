using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Persists webhook settings via <see cref="IDataStore"/>.
/// Uses in-memory caching to avoid blocking async calls.
/// </summary>
public class WebhookSettingsPersistenceService
{
    private const string Category = "webhook-settings";
    private readonly IDataStore _store;
    private readonly ILogger<WebhookSettingsPersistenceService> _logger;
    private WebhookSettingsData? _cachedData;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private bool _initialized;

    public WebhookSettingsPersistenceService(IDataStore store, ILogger<WebhookSettingsPersistenceService> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Preloads webhook settings into memory cache during startup.
    /// </summary>
    public async Task PreloadAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            _cachedData = await LoadInternalAsync(ct);
            _initialized = true;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task<WebhookSettingsData?> LoadInternalAsync(CancellationToken ct)
    {
        try
        {
            return await _store.GetDocumentAsync<WebhookSettingsData>(Category, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load webhook settings from store");
            return null;
        }
    }

    /// <summary>Load webhook settings from store.</summary>
    public WebhookSettingsData? Load()
    {
        if (_initialized && _cachedData != null)
            return _cachedData;

        try
        {
            _cachedData = _store.GetDocumentAsync<WebhookSettingsData>(Category).GetAwaiter().GetResult();
            _initialized = true;
            return _cachedData;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load webhook settings from store");
            return null;
        }
    }

    /// <summary>Save webhook settings to store.</summary>
    public bool Save(WebhookSettingsData data)
    {
        try
        {
            _store.SetDocumentAsync(Category, data).GetAwaiter().GetResult();
            _cachedData = data;
            _logger.LogInformation("Webhook settings saved");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save webhook settings");
            return false;
        }
    }
}

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
    /// List of enabled webhook event types. When empty or null, all events are enabled (backward compatible).
    /// Supported values: AddRoute, UpdateRoute, RemoveRoute, AddCluster, UpdateCluster, RemoveCluster, RenameCluster, RollbackConfig.
    /// </summary>
    public List<string>? EnabledEvents { get; set; }
}
