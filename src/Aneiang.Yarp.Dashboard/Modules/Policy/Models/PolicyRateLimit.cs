namespace Aneiang.Yarp.Dashboard.Modules.Policy.Models;

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
