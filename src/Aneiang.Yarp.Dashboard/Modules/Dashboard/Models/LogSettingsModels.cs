using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Models;

/// <summary>
/// Current proxy-log runtime settings (hot-reloadable subset).
/// </summary>
public sealed class LogSettingsResponse
{
    [JsonPropertyName("persistenceEnabled")]
    public bool PersistenceEnabled { get; set; }

    [JsonPropertyName("metaRetentionDays")]
    public int MetaRetentionDays { get; set; }

    [JsonPropertyName("bodyRetentionDays")]
    public int BodyRetentionDays { get; set; }

    [JsonPropertyName("requestBodyCaptureEnabled")]
    public bool RequestBodyCaptureEnabled { get; set; }

    [JsonPropertyName("responseBodyCaptureEnabled")]
    public bool ResponseBodyCaptureEnabled { get; set; }

    [JsonPropertyName("maxBodyLength")]
    public int MaxBodyLength { get; set; }

    [JsonPropertyName("maxBodyBufferBytes")]
    public int MaxBodyBufferBytes { get; set; }

    [JsonPropertyName("samplingEnabled")]
    public bool SamplingEnabled { get; set; }

    [JsonPropertyName("samplingRate")]
    public double SamplingRate { get; set; }

    [JsonPropertyName("errorsOnly")]
    public bool ErrorsOnly { get; set; }

    /// <summary>Log level name: Trace / Information / Warning / Error / Critical.</summary>
    [JsonPropertyName("minLogLevel")]
    public string MinLogLevel { get; set; } = "Information";

    /// <summary>True when the current values have been overridden (persisted) rather than loaded from appsettings.</summary>
    [JsonPropertyName("isCustomized")]
    public bool IsCustomized { get; set; }
}

/// <summary>
/// Partial update request for proxy-log runtime settings. Only non-null fields are applied.
/// </summary>
public sealed class LogSettingsUpdateRequest
{
    [JsonPropertyName("persistenceEnabled")]
    public bool? PersistenceEnabled { get; set; }

    [JsonPropertyName("metaRetentionDays")]
    public int? MetaRetentionDays { get; set; }

    [JsonPropertyName("bodyRetentionDays")]
    public int? BodyRetentionDays { get; set; }

    [JsonPropertyName("requestBodyCaptureEnabled")]
    public bool? RequestBodyCaptureEnabled { get; set; }

    [JsonPropertyName("responseBodyCaptureEnabled")]
    public bool? ResponseBodyCaptureEnabled { get; set; }

    [JsonPropertyName("maxBodyLength")]
    public int? MaxBodyLength { get; set; }

    [JsonPropertyName("maxBodyBufferBytes")]
    public int? MaxBodyBufferBytes { get; set; }

    [JsonPropertyName("samplingEnabled")]
    public bool? SamplingEnabled { get; set; }

    [JsonPropertyName("samplingRate")]
    public double? SamplingRate { get; set; }

    [JsonPropertyName("errorsOnly")]
    public bool? ErrorsOnly { get; set; }

    [JsonPropertyName("minLogLevel")]
    public string? MinLogLevel { get; set; }
}

/// <summary>
/// Read-only proxy-log options that require a restart to take effect.
/// These are surfaced in the settings UI so users know where to change them (appsettings.json).
/// </summary>
public sealed class LogRestartRequiredOptions
{
    [JsonPropertyName("bufferCapacity")]
    public int BufferCapacity { get; set; }

    [JsonPropertyName("enableAsyncLogging")]
    public bool EnableAsyncLogging { get; set; }

    [JsonPropertyName("headerBlacklist")]
    public List<string>? HeaderBlacklist { get; set; }

    [JsonPropertyName("queryBlacklist")]
    public List<string>? QueryBlacklist { get; set; }

    [JsonPropertyName("jsonFieldSanitizeList")]
    public List<string>? JsonFieldSanitizeList { get; set; }
}
