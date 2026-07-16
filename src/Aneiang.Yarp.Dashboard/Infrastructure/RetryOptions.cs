namespace Aneiang.Yarp.Dashboard.Infrastructure;

/// <summary>
/// Retry policy configuration options.
/// Can be configured globally in DashboardOptions or per-route via metadata.
/// </summary>
public class RetryOptions
{
    /// <summary>Enable retry for failed proxy requests. Default: false.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Maximum number of retry attempts.
    /// Route metadata key: Retry:MaxRetries. Default: 3.
    /// </summary>
    public int DefaultMaxRetries { get; set; } = 3;

    /// <summary>
    /// Base backoff delay in milliseconds (exponential: base * 2^attempt).
    /// Route metadata key: Retry:BackoffBaseMs. Default: 100.
    /// </summary>
    public int BackoffBaseMs { get; set; } = 100;

    /// <summary>
    /// Maximum jitter to add to backoff delay in milliseconds.
    /// Route metadata key: Retry:BackoffJitterMs. Default: 50.
    /// </summary>
    public int BackoffJitterMs { get; set; } = 50;

    /// <summary>
    /// Maximum total time allowed for retries in seconds.
    /// Route metadata key: Retry:TimeoutSeconds. Default: 30.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Whether to try different destinations on retry.
    /// Route metadata key: Retry:UseDifferentDestination. Default: false.
    /// </summary>
    public bool UseDifferentDestination { get; set; } = false;

    /// <summary>
    /// Whether to retry non-idempotent requests (POST, PATCH).
    /// Route metadata key: Retry:RetryNonIdempotent. Default: false.
    /// </summary>
    public bool RetryNonIdempotent { get; set; } = false;

    /// <summary>
    /// Status codes that trigger a retry.
    /// Route metadata key: Retry:RetryOnStatusCodes (comma-separated). Default: 502,503,504.
    /// </summary>
    public List<int> DefaultRetryStatusCodes { get; set; } = new() { 502, 503, 504 };
}
