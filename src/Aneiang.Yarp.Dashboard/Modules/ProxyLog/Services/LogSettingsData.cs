namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>
/// Data model for log settings as returned to the UI.
/// </summary>
public class LogSettingsData
{
    public bool LogPersistenceEnabled { get; set; }
    public int LogMetaRetentionDays { get; set; }
    public int LogBodyRetentionDays { get; set; }
    public bool EnableProxyRequestBodyCapture { get; set; }
    public bool EnableProxyResponseBodyCapture { get; set; }
    public int LogMaxBodyLength { get; set; }
    public bool EnableLogSampling { get; set; }
    public double LogSamplingRate { get; set; }
    public bool LogErrorsOnly { get; set; }
    public string MinLogLevel { get; set; } = "Debug";
    public int LogBufferCapacity { get; set; }
    public int LogMaxBodyBufferBytes { get; set; }

    /// <summary>Whether settings require a restart to take full effect.</summary>
    public bool RequiresRestart => false;
}
