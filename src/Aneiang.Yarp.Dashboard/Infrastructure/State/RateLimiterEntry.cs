using System.Threading.RateLimiting;

namespace Aneiang.Yarp.Dashboard.Infrastructure.State;

/// <summary>Wrapper tracking last access time for stale-eviction cleanup strategy.</summary>
public sealed class RateLimiterEntry
{
    public RateLimiter Limiter { get; }
    public DateTime LastAccessedAt { get; set; }

    public RateLimiterEntry(RateLimiter limiter)
    {
        Limiter = limiter;
        LastAccessedAt = DateTime.Now;
    }
}
