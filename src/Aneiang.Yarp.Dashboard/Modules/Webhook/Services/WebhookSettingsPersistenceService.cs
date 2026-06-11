using System.Text.Json;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Dashboard.Modules.Waf.Models;
using Aneiang.Yarp.Dashboard.Modules.Policy.Models;
using Aneiang.Yarp.Dashboard.Modules.Alert.Models;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Aneiang.Yarp.Dashboard.Modules.Webhook.Models;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.Webhook.Services;

/// <summary>
/// Persists webhook settings via <see cref="IStructuredDataStore"/>.
/// Uses in-memory caching to avoid blocking async calls.
/// </summary>
public class WebhookSettingsPersistenceService : IWebhookSettingsPersistenceService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IStructuredDataStore _store;
    private readonly ILogger<WebhookSettingsPersistenceService> _logger;
    private WebhookSettingsData? _cachedData;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private bool _initialized;

    public WebhookSettingsPersistenceService(IStructuredDataStore store, ILogger<WebhookSettingsPersistenceService> logger)
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
            var entity = await _store.GetWebhookSettingsAsync(ct);
            if (entity == null) return null;

            return new WebhookSettingsData
            {
                DingTalkEndpoints = ParseEndpoints(entity.Endpoints),
                GenericEndpoints = new List<WebhookEndpoint>(),
                EnabledEvents = ParseEvents(entity.Events)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load webhook settings from structured store");
            return null;
        }
    }

    private static List<WebhookEndpoint> ParseEndpoints(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new List<WebhookEndpoint>();
        try
        {
            return JsonSerializer.Deserialize<List<WebhookEndpoint>>(json, _jsonOptions) ?? new List<WebhookEndpoint>();
        }
        catch
        {
            return new List<WebhookEndpoint>();
        }
    }

    private static List<string>? ParseEvents(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Load webhook settings from store.</summary>
    public WebhookSettingsData? Load()
    {
        if (_initialized && _cachedData != null)
            return _cachedData;

        return LoadInternalAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>Load webhook settings from store asynchronously.</summary>
    public async Task<WebhookSettingsData?> LoadAsync(CancellationToken ct = default)
    {
        if (_initialized && _cachedData != null)
            return _cachedData;

        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_initialized && _cachedData != null)
                return _cachedData;

            _cachedData = await LoadInternalAsync(ct);
            _initialized = true;
            return _cachedData;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>Save webhook settings to store.</summary>
    public bool Save(WebhookSettingsData data)
    {
        return SaveCoreAsync(data, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>Save webhook settings to store asynchronously.</summary>
    public async Task<bool> SaveAsync(WebhookSettingsData data, CancellationToken ct = default)
    {
        return await SaveCoreAsync(data, ct);
    }

    private async Task<bool> SaveCoreAsync(WebhookSettingsData data, CancellationToken ct)
    {
        try
        {
            var entity = new WebhookSettingsEntity
            {
                Enabled = data.DingTalkEndpoints?.Any(e => !string.IsNullOrEmpty(e.Url)) == true ||
                         data.GenericEndpoints?.Any(e => !string.IsNullOrEmpty(e.Url)) == true,
                Endpoints = JsonSerializer.Serialize(data.DingTalkEndpoints, _jsonOptions),
                Events = data.EnabledEvents != null ? JsonSerializer.Serialize(data.EnabledEvents, _jsonOptions) : null,
                TimeoutSeconds = data.TimeoutSeconds > 0 ? data.TimeoutSeconds : 10,
                RetryCount = data.RetryCount >= 0 ? data.RetryCount : 1,
                Secret = data.DingTalkEndpoints?.FirstOrDefault()?.Secret,
                UpdatedAt = DateTime.UtcNow
            };

            await _store.SaveWebhookSettingsAsync(entity, ct);
            _cachedData = data;
            _logger.LogInformation("Webhook settings saved to structured store");
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

    /// <summary>HTTP request timeout in seconds. Default 10.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Number of retry attempts on failure. Default 1.</summary>
    public int RetryCount { get; set; } = 1;
}
