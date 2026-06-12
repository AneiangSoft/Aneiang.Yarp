using System.Text.Json;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.Webhook.Services;

/// <summary>
/// Persists webhook settings via <see cref="IGatewayRepository"/>.
/// </summary>
public class WebhookSettingsPersistenceService : IWebhookSettingsPersistenceService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IGatewayRepository _repository;
    private readonly ILogger<WebhookSettingsPersistenceService> _logger;
    private WebhookSettingsData? _cachedData;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private bool _initialized;

    public WebhookSettingsPersistenceService(IGatewayRepository repository, ILogger<WebhookSettingsPersistenceService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>Preloads webhook settings into memory cache during startup.</summary>
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
        finally { _cacheLock.Release(); }
    }

    private async Task<WebhookSettingsData?> LoadInternalAsync(CancellationToken ct)
    {
        try
        {
            var entity = await _repository.GetWebhookSettingsAsync(ct);
            if (entity == null) return null;

            return new WebhookSettingsData
            {
                DingTalkEndpoints = ParseEndpoints(entity.Endpoints),
                GenericEndpoints = ParseEndpoints(entity.GenericEndpoints),
                EnabledEvents = ParseEvents(entity.Events),
                TimeoutSeconds = entity.TimeoutSeconds > 0 ? entity.TimeoutSeconds : 10,
                RetryCount = entity.RetryCount >= 0 ? entity.RetryCount : 1,
                AlertConfig = entity.AlertConfig
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load webhook settings from repository");
            return null;
        }
    }

    private static List<WebhookEndpoint> ParseEndpoints(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new List<WebhookEndpoint>();
        try { return JsonSerializer.Deserialize<List<WebhookEndpoint>>(json, _jsonOptions) ?? new List<WebhookEndpoint>(); }
        catch { return new List<WebhookEndpoint>(); }
    }

    private static List<string>? ParseEvents(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<List<string>>(json, _jsonOptions); }
        catch { return null; }
    }

    public WebhookSettingsData? Load()
    {
        if (_initialized && _cachedData != null) return _cachedData;
        return LoadInternalAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task<WebhookSettingsData?> LoadAsync(CancellationToken ct = default)
    {
        if (_initialized && _cachedData != null) return _cachedData;
        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_initialized && _cachedData != null) return _cachedData;
            _cachedData = await LoadInternalAsync(ct);
            _initialized = true;
            return _cachedData;
        }
        finally { _cacheLock.Release(); }
    }

    public bool Save(WebhookSettingsData data)
    {
        return SaveCoreAsync(data, CancellationToken.None).GetAwaiter().GetResult();
    }

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
                Endpoints = data.DingTalkEndpoints?.Count > 0
                    ? JsonSerializer.Serialize(data.DingTalkEndpoints, _jsonOptions) : null,
                GenericEndpoints = data.GenericEndpoints?.Count > 0
                    ? JsonSerializer.Serialize(data.GenericEndpoints, _jsonOptions) : null,
                Events = data.EnabledEvents != null && data.EnabledEvents.Count > 0
                    ? JsonSerializer.Serialize(data.EnabledEvents, _jsonOptions) : null,
                TimeoutSeconds = data.TimeoutSeconds > 0 ? data.TimeoutSeconds : 10,
                RetryCount = data.RetryCount >= 0 ? data.RetryCount : 1,
                Secret = data.DingTalkEndpoints?.FirstOrDefault()?.Secret,
                AlertConfig = data.AlertConfig,
                UpdatedAt = DateTime.UtcNow
            };

            await _repository.SaveWebhookSettingsAsync(entity, ct);
            _cachedData = data;
            _logger.LogInformation("Webhook settings saved to repository");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save webhook settings");
            return false;
        }
    }
}
