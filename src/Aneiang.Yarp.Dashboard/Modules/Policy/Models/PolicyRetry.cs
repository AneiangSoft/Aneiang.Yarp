namespace Aneiang.Yarp.Dashboard.Modules.Policy.Models;

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
