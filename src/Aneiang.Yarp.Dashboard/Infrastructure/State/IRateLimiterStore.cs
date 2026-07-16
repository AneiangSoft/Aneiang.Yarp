using System.Threading.RateLimiting;

namespace Aneiang.Yarp.Dashboard.Infrastructure.State;

/// <summary>
/// Manages in-memory rate limiter instances. Registered as a Singleton so that
/// rate limiter lifecycle (creation, cleanup, disposal) is controlled by DI.
/// </summary>
public interface IRateLimiterStore
{
    /// <summary>Get or create a rate limiter wrapper for the given key.</summary>
    RateLimiterEntry GetOrAdd(string key, Func<RateLimiter> factory);

    /// <summary>Current number of limiters.</summary>
    int Count { get; }

    /// <summary>
    /// Evict stale entries (not accessed within <paramref name="staleThreshold"/>),
    /// then if count still exceeds <paramref name="maxCount"/>, evict oldest half.
    /// Disposes evicted limiters.
    /// </summary>
    void Cleanup(TimeSpan staleThreshold, int maxCount);
}
