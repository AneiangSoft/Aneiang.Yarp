namespace Aneiang.Yarp.Dashboard.Modules.Policy.Models;

/// <summary>
/// Route policy template: contains retry, rate-limit, WAF toggle.
/// Can be applied to one or more routes via route metadata.
/// </summary>
public class RoutePolicy
{
    /// <summary>Internal immutable policy UID.</summary>
    public string PolicyUid { get; set; } = string.Empty;

    /// <summary>Unique identifier for this policy. Kept for compatibility; prefer PolicyKey for new code.</summary>
    public string PolicyId { get; set; } = string.Empty;

    /// <summary>Policy key alias.</summary>
    public string PolicyKey
    {
        get => PolicyId;
        set => PolicyId = value;
    }

    /// <summary>Display name of this policy.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Description of what this policy does.</summary>
    public string? Description { get; set; }

    /// <summary>Is this policy enabled. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>When this policy was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Route IDs this policy is applied to (read-only, maintained by system).</summary>
    public List<string> AppliedRoutes { get; set; } = new();

    /// <summary>Retry settings for this policy.</summary>
    public PolicyRetry? Retry { get; set; }

    /// <summary>Rate limit settings for this policy.</summary>
    public PolicyRateLimit? RateLimit { get; set; }

    /// <summary>WAF route-level toggle: true=force on, false=force off, null=follow global default.</summary>
    public bool? WafEnabled { get; set; }

    /// <summary>Generate metadata entries for route configuration.</summary>
    public Dictionary<string, string> ToMetadata()
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (Retry != null)
        {
            foreach (var kvp in Retry.ToMetadata())
                metadata[kvp.Key] = kvp.Value;
        }

        if (RateLimit != null)
        {
            foreach (var kvp in RateLimit.ToMetadata())
                metadata[kvp.Key] = kvp.Value;
        }

        if (WafEnabled.HasValue)
        {
            metadata["Waf:Enabled"] = WafEnabled.Value.ToString().ToLowerInvariant();
        }

        metadata["Policy:Id"] = PolicyId;
        metadata["Policy:Name"] = DisplayName;

        return metadata;
    }
}

/// <summary>
/// Cluster policy template: contains circuit breaker configuration.
/// Can be applied to one or more clusters.
/// </summary>
public class ClusterPolicy
{
    /// <summary>Internal immutable policy UID.</summary>
    public string PolicyUid { get; set; } = string.Empty;

    /// <summary>Unique identifier for this policy. Kept for compatibility; prefer PolicyKey for new code.</summary>
    public string PolicyId { get; set; } = string.Empty;

    /// <summary>Policy key alias.</summary>
    public string PolicyKey
    {
        get => PolicyId;
        set => PolicyId = value;
    }

    /// <summary>Display name of this policy.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Description of what this policy does.</summary>
    public string? Description { get; set; }

    /// <summary>Is this policy enabled. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>When this policy was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Cluster IDs this policy is applied to (read-only, maintained by system).</summary>
    public List<string> AppliedClusters { get; set; } = new();

    /// <summary>Circuit breaker settings for this policy.</summary>
    public PolicyCircuitBreaker? CircuitBreaker { get; set; }

    /// <summary>Convert to CircuitBreakerConfig for cluster configuration.</summary>
    public Aneiang.Yarp.Models.CircuitBreakerConfig? ToCircuitBreakerConfig()
    {
        if (CircuitBreaker == null || !CircuitBreaker.Enabled)
            return null;

        return new Aneiang.Yarp.Models.CircuitBreakerConfig
        {
            Enabled = CircuitBreaker.Enabled,
            FailureThreshold = CircuitBreaker.FailureThreshold,
            RecoveryTimeoutSeconds = CircuitBreaker.RecoveryTimeoutSeconds,
            HalfOpenMaxAttempts = CircuitBreaker.HalfOpenMaxAttempts,
            FailureStatusCodes = CircuitBreaker.FailureStatusCodes
        };
    }
}

// ─── Shared Sub-Models ─────────────────────────────────────────────

/// <summary>
/// Circuit breaker settings for a cluster policy.
/// </summary>
public class PolicyCircuitBreaker
{
    /// <summary>Enable circuit breaker. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Number of failures before opening circuit. Default: 5.</summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>Seconds before attempting recovery. Default: 30.</summary>
    public int RecoveryTimeoutSeconds { get; set; } = 30;

    /// <summary>Max requests in half-open state. Default: 1.</summary>
    public int HalfOpenMaxAttempts { get; set; } = 1;

    /// <summary>HTTP status codes that count as failures.</summary>
    public List<int> FailureStatusCodes { get; set; } = new() { 500, 502, 503, 504 };
}

/// <summary>
/// Retry settings for a route policy.
/// </summary>
public class PolicyRetry
{
    /// <summary>Enable retry. Default: false.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Maximum retry attempts. Default: 3.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Base backoff delay in milliseconds. Default: 100.</summary>
    public int BackoffBaseMs { get; set; } = 100;

    /// <summary>Max jitter in milliseconds. Default: 50.</summary>
    public int BackoffJitterMs { get; set; } = 50;

    /// <summary>Retry timeout in seconds. Default: 30.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Try different destinations on retry. Default: false.</summary>
    public bool UseDifferentDestination { get; set; } = false;

    /// <summary>Retry non-idempotent requests. Default: false.</summary>
    public bool RetryNonIdempotent { get; set; } = false;

    /// <summary>Status codes that trigger retry.</summary>
    public List<int> RetryStatusCodes { get; set; } = new() { 502, 503, 504 };

    /// <summary>Get metadata entries for route configuration.</summary>
    public Dictionary<string, string> ToMetadata()
    {
        return new Dictionary<string, string>
        {
            ["Retry:Enabled"] = Enabled.ToString().ToLowerInvariant(),
            ["Retry:MaxRetries"] = MaxRetries.ToString(),
            ["Retry:BackoffBaseMs"] = BackoffBaseMs.ToString(),
            ["Retry:BackoffJitterMs"] = BackoffJitterMs.ToString(),
            ["Retry:TimeoutSeconds"] = TimeoutSeconds.ToString(),
            ["Retry:UseDifferentDestination"] = UseDifferentDestination.ToString().ToLowerInvariant(),
            ["Retry:RetryNonIdempotent"] = RetryNonIdempotent.ToString().ToLowerInvariant(),
            ["Retry:RetryOnStatusCodes"] = string.Join(",", RetryStatusCodes)
        };
    }
}

/// <summary>
/// Rate limit settings for a route policy.
/// </summary>
public class PolicyRateLimit
{
    /// <summary>Enable rate limiting. Default: false.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Algorithm: FixedWindow, SlidingWindow, TokenBucket.</summary>
    public string Algorithm { get; set; } = "FixedWindow";

    /// <summary>Requests per window. Default: 100.</summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>Window duration. Default: "1m".</summary>
    public string Window { get; set; } = "1m";

    /// <summary>Queue limit. Default: 0 (reject immediately when limit exceeded).</summary>
    public int QueueLimit { get; set; } = 0;

    /// <summary>Partition key: IpAddress, UserId, Route, Global.</summary>
    public string PartitionKey { get; set; } = "IpAddress";

    /// <summary>Get metadata entries for route configuration.</summary>
    public Dictionary<string, string> ToMetadata()
    {
        return new Dictionary<string, string>
        {
            ["RateLimit:Enabled"] = Enabled.ToString().ToLowerInvariant(),
            ["RateLimit:Algorithm"] = Algorithm,
            ["RateLimit:PermitLimit"] = PermitLimit.ToString(),
            ["RateLimit:Window"] = Window,
            ["RateLimit:QueueLimit"] = QueueLimit.ToString(),
            ["RateLimit:PartitionKey"] = PartitionKey
        };
    }
}
