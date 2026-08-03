using Aneiang.Yarp.Dashboard.Infrastructure;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>Thread-safe immutable snapshot of log settings used by the proxy hot path.</summary>
public sealed record ProxyLogRuntimeSnapshot(
    bool PersistenceEnabled,
    int MetaRetentionDays,
    int BodyRetentionDays,
    bool RequestBodyCaptureEnabled,
    bool ResponseBodyCaptureEnabled,
    int MaxBodyLength,
    int MaxBodyBufferBytes,
    bool SamplingEnabled,
    double SamplingRate,
    bool ErrorsOnly,
    int MinLogLevelNumeric);

public sealed class ProxyLogRuntimeSettings
{
    private ProxyLogRuntimeSnapshot _current;

    public ProxyLogRuntimeSettings(IOptions<DashboardOptions> options)
    {
        _current = FromOptions(options.Value);
    }

    public ProxyLogRuntimeSnapshot Current => Volatile.Read(ref _current);

    private static ProxyLogRuntimeSnapshot FromOptions(DashboardOptions options) => new(
        options.LogPersistenceEnabled,
        options.LogMetaRetentionDays,
        options.LogBodyRetentionDays,
        options.EnableProxyRequestBodyCapture,
        options.EnableProxyResponseBodyCapture,
        options.LogMaxBodyLength,
        options.LogMaxBodyBufferBytes,
        options.EnableLogSampling,
        options.LogSamplingRate,
        options.LogErrorsOnly,
        ParseLogLevel(options.MinLogLevel));

    private static int ParseLogLevel(string? level) => level switch
    {
        "Critical" => 4,
        "Error" => 3,
        "Warning" => 2,
        "Information" => 1,
        _ => 0
    };
}
