using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Models;

/// <summary>
/// Represents a reusable traffic policy that combines multiple gateway features.
/// Policies can be applied to routes via route metadata.
/// </summary>
public class GatewayPolicy
{
    /// <summary>Unique identifier for this policy.</summary>
    public string PolicyId { get; set; } = string.Empty;

    /// <summary>Display name of this policy.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Description of what this policy does.</summary>
    public string? Description { get; set; }

    /// <summary>When this policy was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Who created this policy.</summary>
    public string? CreatedBy { get; set; }

    /// <summary>Policy priority (higher = evaluated first). Default: 50.</summary>
    public int Priority { get; set; } = 50;

    /// <summary>Is this policy enabled. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    // ─── Feature Settings ──────────────────────────────────────

    /// <summary>Circuit breaker settings for this policy.</summary>
    public PolicyCircuitBreaker? CircuitBreaker { get; set; }

    /// <summary>Retry settings for this policy.</summary>
    public PolicyRetry? Retry { get; set; }

    /// <summary>Rate limit settings for this policy.</summary>
    public PolicyRateLimit? RateLimit { get; set; }

    /// <summary>WAF settings for this policy.</summary>
    public PolicyWaf? Waf { get; set; }

    /// <summary>Custom plugin settings (key = plugin ID, value = plugin-specific JSON).</summary>
    public Dictionary<string, object>? CustomPlugins { get; set; }

    /// <summary>Tags for categorization.</summary>
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// Circuit breaker settings for a policy.
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

    /// <summary>List of status codes that count as failures.</summary>
    public List<int> FailureStatusCodes { get; set; } = new() { 500, 502, 503, 504 };

    /// <summary>Get metadata entries for route configuration.</summary>
    public Dictionary<string, string> ToMetadata()
    {
        return new Dictionary<string, string>
        {
            ["CircuitBreaker:Enabled"] = Enabled.ToString().ToLowerInvariant(),
            ["CircuitBreaker:FailureThreshold"] = FailureThreshold.ToString(),
            ["CircuitBreaker:RecoveryTimeoutSeconds"] = RecoveryTimeoutSeconds.ToString(),
            ["CircuitBreaker:HalfOpenMaxAttempts"] = HalfOpenMaxAttempts.ToString()
        };
    }
}

/// <summary>
/// Retry settings for a policy.
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
/// Rate limit settings for a policy.
/// </summary>
public class PolicyRateLimit
{
    /// <summary>Enable rate limiting. Default: false.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Algorithm: FixedWindow, SlidingWindow, TokenBucket, Concurrency.</summary>
    public string Algorithm { get; set; } = "FixedWindow";

    /// <summary>Requests per window. Default: 100.</summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>Window duration. Default: "1m".</summary>
    public string Window { get; set; } = "1m";

    /// <summary>Queue limit. Default: 10.</summary>
    public int QueueLimit { get; set; } = 10;

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

/// <summary>
/// WAF settings for a policy.
/// </summary>
public class PolicyWaf
{
    /// <summary>Enable WAF for this policy. Default: false.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Block SQL injection attempts. Default: true.</summary>
    public bool BlockSqlInjection { get; set; } = true;

    /// <summary>Block XSS attempts. Default: true.</summary>
    public bool BlockXss { get; set; } = true;

    /// <summary>Block path traversal attempts. Default: true.</summary>
    public bool BlockPathTraversal { get; set; } = true;

    /// <summary>Max request body size. Default: 10MB.</summary>
    public long MaxRequestBodySize { get; set; } = 10 * 1024 * 1024;

    /// <summary>Get metadata entries for route configuration.</summary>
    public Dictionary<string, string> ToMetadata()
    {
        return new Dictionary<string, string>
        {
            ["Waf:Enabled"] = Enabled.ToString().ToLowerInvariant(),
            ["Waf:BlockSqlInjection"] = BlockSqlInjection.ToString().ToLowerInvariant(),
            ["Waf:BlockXss"] = BlockXss.ToString().ToLowerInvariant(),
            ["Waf:BlockPathTraversal"] = BlockPathTraversal.ToString().ToLowerInvariant(),
            ["Waf:MaxRequestBodySize"] = MaxRequestBodySize.ToString()
        };
    }
}

/// <summary>
/// Container for all gateway policies.
/// </summary>
public class GatewayPolicyCollection
{
    /// <summary>List of all policies.</summary>
    public List<GatewayPolicy> Policies { get; set; } = new();

    /// <summary>Last modified timestamp.</summary>
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
}
