namespace Aneiang.Yarp.Plugin.RateLimit.Redis;

/// <summary>Outcome of a single distributed rate-limit acquisition attempt.</summary>
/// <param name="Allowed">Whether the request is within the configured budget.</param>
/// <param name="Limit">The configured capacity (for response headers).</param>
/// <param name="Remaining">Remaining units after this attempt.</param>
/// <param name="RetryAfterSeconds">Seconds until the caller may retry (0 when allowed).</param>
public readonly record struct DistributedRateLimitResult(bool Allowed, long Limit, long Remaining, long RetryAfterSeconds);

/// <summary>
/// Abstraction over a distributed counter store used by the Redis rate-limit plugin.
/// Implementations must be thread-safe and must degrade gracefully (fail-open)
/// when the backing store is unavailable so the gateway stays reachable.
/// </summary>
public interface IDistributedRateLimitStore
{
    /// <summary>
    /// Attempts to consume one unit of capacity for <paramref name="key"/> using the
    /// requested algorithm (FixedWindow | SlidingWindow | TokenBucket).
    /// </summary>
    /// <param name="algorithm">Algorithm selector: FixedWindow, SlidingWindow or TokenBucket.</param>
    /// <param name="key">Fully-qualified counter key, e.g. "aneiang:rl:{route}:{client}".</param>
    /// <param name="limit">Requests allowed per window (bucket capacity before burst).</param>
    /// <param name="windowSeconds">Window length in seconds.</param>
    /// <param name="burstBalance">Extra burst capacity on top of <paramref name="limit"/> (TokenBucket only).</param>
    /// <param name="redisConnectionString">
    /// Optional per-route Redis connection string. Implementations may fall back to a
    /// process-wide default when null or empty.
    /// </param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    ValueTask<DistributedRateLimitResult> TryAcquireAsync(
        string algorithm,
        string key,
        long limit,
        int windowSeconds,
        int burstBalance,
        string? redisConnectionString,
        CancellationToken cancellationToken = default);
}
