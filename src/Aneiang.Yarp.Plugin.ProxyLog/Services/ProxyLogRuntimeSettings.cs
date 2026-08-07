using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Plugin.ProxyLog.Services;

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

    public ProxyLogRuntimeSettings(IOptions<ProxyLogPluginOptions> options)
    {
        _current = FromOptions(options.Value);
    }

    public ProxyLogRuntimeSnapshot Current => Volatile.Read(ref _current);

    /// <summary>
    /// Atomically replace the current snapshot with a new one.
    /// Called by the settings API to hot-reload ProxyLog configuration without restart.
    /// </summary>
    public void Update(ProxyLogRuntimeSnapshot snapshot)
    {
        Volatile.Write(ref _current, snapshot);
    }

    /// <summary>
    /// Atomically replace the current snapshot by applying changes via the selector function.
    /// </summary>
    public void Update(Func<ProxyLogRuntimeSnapshot, ProxyLogRuntimeSnapshot> updater)
    {
        var current = Volatile.Read(ref _current);
        Volatile.Write(ref _current, updater(current));
    }

    private static ProxyLogRuntimeSnapshot FromOptions(ProxyLogPluginOptions options) => new(
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
