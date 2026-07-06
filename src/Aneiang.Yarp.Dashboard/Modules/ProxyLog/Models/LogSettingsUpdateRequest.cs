namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;

/// <summary>
/// Request model for updating log persistence settings via the Settings UI.
/// All fields are optional — only provided fields will be updated.
/// </summary>
public class LogSettingsUpdateRequest
{
    /// <summary>Enable or disable log persistence to SQLite. Default: true.</summary>
    public bool? LogPersistenceEnabled { get; set; }

    /// <summary>Number of days to retain lightweight log metadata. Minimum: 1, maximum: 365.</summary>
    public int? LogMetaRetentionDays { get; set; }

    /// <summary>Number of days to retain log body details. Must be ≤ LogMetaRetentionDays. Minimum: 1.</summary>
    public int? LogBodyRetentionDays { get; set; }

    /// <summary>Enable or disable request body capture. Default: false.</summary>
    public bool? EnableProxyRequestBodyCapture { get; set; }

    /// <summary>Enable or disable response body capture. Default: false.</summary>
    public bool? EnableProxyResponseBodyCapture { get; set; }

    /// <summary>Maximum request/response body length to log (bytes). Minimum: 256, maximum: 1048576.</summary>
    public int? LogMaxBodyLength { get; set; }

    /// <summary>Enable or disable log sampling. Default: false.</summary>
    public bool? EnableLogSampling { get; set; }

    /// <summary>Sampling rate (0.0 to 1.0). Only effective when EnableLogSampling is true.</summary>
    public double? LogSamplingRate { get; set; }

    /// <summary>Only log error requests (status code >= 400). Default: false.</summary>
    public bool? LogErrorsOnly { get; set; }

    /// <summary>Minimum log level. Supported: Debug, Information, Warning, Error, Critical.</summary>
    public string? MinLogLevel { get; set; }

    /// <summary>In-memory log buffer capacity. Minimum: 16. ⚠️ Requires restart to take effect.</summary>
    public int? LogBufferCapacity { get; set; }
}
