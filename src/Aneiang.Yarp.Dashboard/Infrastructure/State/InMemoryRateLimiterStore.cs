using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace Aneiang.Yarp.Dashboard.Infrastructure.State;

/// <summary>In-memory singleton implementation of <see cref="IRateLimiterStore"/>.</summary>
public sealed class InMemoryRateLimiterStore : IRateLimiterStore
{
    private readonly ConcurrentDictionary<string, RateLimiterEntry> _limiters = new();

    public int Count => _limiters.Count;

    public RateLimiterEntry GetOrAdd(string key, Func<RateLimiter> factory)
        => _limiters.GetOrAdd(key, _ => new RateLimiterEntry(factory()));

    public void Cleanup(TimeSpan staleThreshold, int maxCount)
    {
        var now = DateTime.Now;

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
