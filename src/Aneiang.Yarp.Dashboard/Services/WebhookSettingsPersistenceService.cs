using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Persists webhook settings via <see cref="IDataStore"/>.
/// </summary>
public class WebhookSettingsPersistenceService
{
    private const string Category = "webhook-settings";
    private readonly IDataStore _store;
    private readonly ILogger<WebhookSettingsPersistenceService> _logger;

    public WebhookSettingsPersistenceService(IDataStore store, ILogger<WebhookSettingsPersistenceService> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>Load webhook settings from store.</summary>
    public WebhookSettingsData? Load()
    {
        try
        {
            return _store.GetDocumentAsync<WebhookSettingsData>(Category).GetAwaiter().GetResult();
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
