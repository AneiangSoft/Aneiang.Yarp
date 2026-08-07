namespace Aneiang.Yarp.Plugin.ProxyLog;

/// <summary>
/// Plugin-level options for ProxyLog, mapped from DashboardOptions during DI registration.
/// </summary>
public sealed class ProxyLogPluginOptions
{
    public string RoutePrefix { get; set; } = "dashboard";
    public bool LogPersistenceEnabled { get; set; } = true;
    public int LogMetaRetentionDays { get; set; } = 7;
    public int LogBodyRetentionDays { get; set; } = 3;
    public bool EnableProxyRequestBodyCapture { get; set; }
    public bool EnableProxyResponseBodyCapture { get; set; }
    public int LogMaxBodyLength { get; set; } = 8192;
    public int LogMaxBodyBufferBytes { get; set; } = 65536;
    public bool EnableLogSampling { get; set; }
    public double LogSamplingRate { get; set; } = 1.0;
    public bool LogErrorsOnly { get; set; }
    public string? MinLogLevel { get; set; } = "Information";
    public int LogBufferCapacity { get; set; } = 4096;
    public int LogBufferDrainIntervalSeconds { get; set; } = 5;
    public int LogBufferMaxBatchSize { get; set; } = 100;
    public int LogQueryDefaultPageSize { get; set; } = 50;
    public int LogQueryMaxPageSize { get; set; } = 500;
    public int LogQueryMaxResults { get; set; } = 10000;
    public int LogQueryTimeoutSeconds { get; set; } = 30;
    public List<string> LogHeaderBlacklist { get; set; } = [];
    public List<string> LogQueryBlacklist { get; set; } = [];
    public List<string> LogJsonFieldSanitizeList { get; set; } = [];
}
