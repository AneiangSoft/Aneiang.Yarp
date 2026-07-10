using System.Collections.Concurrent;
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

/// <summary>Wrapper tracking last access time for stale-eviction cleanup strategy.</summary>
public sealed class RateLimiterEntry
{
    public RateLimiter Limiter { get; }
    public DateTime LastAccessedAt { get; set; }

    public RateLimiterEntry(RateLimiter limiter)
    {
        Limiter = limiter;
        LastAccessedAt = DateTime.UtcNow;
    }
}

/// <summary>In-memory singleton implementation of <see cref="IRateLimiterStore"/>.</summary>
public sealed class InMemoryRateLimiterStore : IRateLimiterStore
{
    private readonly ConcurrentDictionary<string, RateLimiterEntry> _limiters = new();

    public int Count => _limiters.Count;

    public RateLimiterEntry GetOrAdd(string key, Func<RateLimiter> factory)
        => _limiters.GetOrAdd(key, _ => new RateLimiterEntry(factory()));

    public void Cleanup(TimeSpan staleThreshold, int maxCount)
    {
        var now = DateTime.UtcNow;

        // Evict stale entries
        foreach (var kvp in _limiters)
        {
            if (now - kvp.Value.LastAccessedAt > staleThreshold)
            {
                if (_limiters.TryRemove(kvp.Key, out var wrapper))
                    wrapper.Limiter.Dispose();
            }
        }

        // Safety fallback: if still too many, evict oldest half
        if (_limiters.Count > maxCount)
        {
            var oldestKeys = _limiters.OrderBy(k => k.Value.LastAccessedAt)
                .Take(_limiters.Count / 2)
                .Select(k => k.Key)
                .ToList();
            foreach (var k in oldestKeys)
            {
                if (_limiters.TryRemove(k, out var wrapper))
                    wrapper.Limiter.Dispose();
            }
        }
    }
}
