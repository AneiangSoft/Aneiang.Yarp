using Aneiang.Yarp.Dashboard.Infrastructure;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>
/// Sampling decision engine. Uses ThreadLocal Random to avoid contention.
/// </summary>
public sealed class LogSampler : ILogSampler
{
    private static readonly ThreadLocal<Random> ThreadRandom = new(() => new Random());
    private readonly bool _enabled;
    private readonly double _rate;

    public LogSampler(IOptions<DashboardOptions> options)
    {
        var opt = options.Value;
        _enabled = opt.EnableLogSampling;
        _rate = opt.LogSamplingRate;
    }

    public bool ShouldSample()
    {
        if (!_enabled || _rate >= 1.0) return true;
        return ThreadRandom.Value!.NextDouble() <= _rate;
    }
}
