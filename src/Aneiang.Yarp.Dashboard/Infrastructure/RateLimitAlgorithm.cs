namespace Aneiang.Yarp.Dashboard.Infrastructure;

/// <summary>
/// Rate limiting algorithm type.
/// </summary>
public enum RateLimitAlgorithm
{
    /// <summary>Fixed window counter algorithm.</summary>
    FixedWindow,
    /// <summary>Sliding window log algorithm.</summary>
    SlidingWindow,
    /// <summary>Token bucket algorithm.</summary>
    TokenBucket,
    /// <summary>Concurrency limit (max parallel requests).</summary>
    Concurrency
}
