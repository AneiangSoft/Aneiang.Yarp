using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.Notification.Models;

/// <summary>
/// Global notification settings response.
/// </summary>
public class GlobalSettingsResponse
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("maxHistoryRecords")]
    public int MaxHistoryRecords { get; set; } = 500;

    [JsonPropertyName("defaultTimeoutSeconds")]
    public int DefaultTimeoutSeconds { get; set; } = 10;

    [JsonPropertyName("defaultRetryCount")]
    public int DefaultRetryCount { get; set; } = 1;
}
