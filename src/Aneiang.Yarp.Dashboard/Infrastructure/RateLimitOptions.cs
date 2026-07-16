namespace Aneiang.Yarp.Dashboard.Infrastructure;

/// <summary>
/// Rate limiting configuration options.
/// Can be configured globally in DashboardOptions or per-route via metadata.
/// </summary>
public class RateLimitOptions
{
    /// <summary>Enable rate limiting for proxy routes. Default: false.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Rate limiting algorithm. Default: FixedWindow.</summary>
    public RateLimitAlgorithm Algorithm { get; set; } = RateLimitAlgorithm.FixedWindow;

    /// <summary>
    /// Maximum requests allowed per time window (FixedWindow/SlidingWindow).
    /// Route metadata key: RateLimit:PermitLimit. Default: 100.
    /// </summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>
    /// Time window duration. Default: "1m" (1 minute).
    /// Route metadata key: RateLimit:Window. Default: "1m".
    /// </summary>
    public string Window { get; set; } = "1m";

    /// <summary>
    /// Number of queued requests when limit is exceeded. Default: 0 (reject immediately).
    /// Route metadata key: RateLimit:QueueLimit. Default: 0.
    /// </summary>
    public int QueueLimit { get; set; } = 0;

    /// <summary>
    /// Token bucket: bucket capacity. Route metadata key: RateLimit:TokenBucketCapacity. Default: 100.
    /// </summary>
    public int TokenBucketCapacity { get; set; } = 100;

    /// <summary>
    /// Token bucket: tokens added per second. Route metadata key: RateLimit:TokenBucketRefillRate. Default: 10.
    /// </summary>
    public double TokenBucketRefillRate { get; set; } = 10;

    /// <summary>
    /// Concurrency limit: maximum parallel requests. Route metadata key: RateLimit:ConcurrencyLimit. Default: 50.
    /// </summary>
    public int ConcurrencyLimit { get; set; } = 50;

    /// <summary>
    /// Partition key for rate limiting: "IpAddress", "UserId", "Route", or "Global".
    /// Route metadata key: RateLimit:PartitionKey. Default: "IpAddress".
    /// </summary>
    public string PartitionKey { get; set; } = "IpAddress";
}
