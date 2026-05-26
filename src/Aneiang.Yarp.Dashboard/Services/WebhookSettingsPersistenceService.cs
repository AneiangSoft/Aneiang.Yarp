using System.Text.Json;
using Aneiang.Yarp.Dashboard.Models;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Persists webhook settings to a JSON file.
/// </summary>
public class WebhookSettingsPersistenceService
{
    private readonly string _filePath;
    private readonly ILogger<WebhookSettingsPersistenceService> _logger;
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public WebhookSettingsPersistenceService(string filePath, ILogger<WebhookSettingsPersistenceService> logger)
    {
        _filePath = filePath;
        _logger = logger;
    }

    /// <summary>Load webhook settings from file.</summary>
    public WebhookSettingsData? Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return null;

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<WebhookSettingsData>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load webhook settings from {FilePath}", _filePath);
            return null;
        }
    }

    /// <summary>Save webhook settings to file.</summary>
    public bool Save(WebhookSettingsData data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            File.WriteAllText(_filePath, json);
            _logger.LogInformation("Webhook settings saved to {FilePath}", _filePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save webhook settings to {FilePath}", _filePath);
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
}
