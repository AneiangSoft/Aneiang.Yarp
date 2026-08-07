using Aneiang.Yarp.Infrastructure.Middleware;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Plugin.ProxyLog.Services;

/// <summary>
/// Sampling decision engine. Uses ThreadLocal Random to avoid contention.
/// </summary>
public sealed class LogSampler : ILogSampler
{
    private static readonly ThreadLocal<Random> ThreadRandom = new(() => new Random());
    private readonly ProxyLogRuntimeSettings _runtimeSettings;

    public LogSampler(ProxyLogRuntimeSettings runtimeSettings)
    {
        _runtimeSettings = runtimeSettings;
    }

    public bool ShouldSample()
    {
        var settings = _runtimeSettings.Current;
        if (!settings.SamplingEnabled || settings.SamplingRate >= 1.0) return true;
        return ThreadRandom.Value!.NextDouble() <= settings.SamplingRate;
    }
}
