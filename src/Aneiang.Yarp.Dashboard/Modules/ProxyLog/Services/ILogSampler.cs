namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>
/// Determines whether a given request should be sampled (included in logs).
/// </summary>
public interface ILogSampler
{
    /// <summary>Returns <c>true</c> if the current request should be included.</summary>
    bool ShouldSample();
}
