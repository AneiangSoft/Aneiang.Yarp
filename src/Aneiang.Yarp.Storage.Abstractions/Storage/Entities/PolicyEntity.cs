namespace Aneiang.Yarp.Storage;

/// <summary>Gateway Policy entity for database storage.</summary>
public class PolicyEntity
{
    public string PolicyUid { get; set; } = Guid.NewGuid().ToString("N");
    public string PolicyId { get; set; } = string.Empty;
    public string PolicyKey
    {
        get => PolicyId;
        set => PolicyId = value;
    }
    /// <summary>"route" | "cluster"</summary>
    public string PolicyType { get; set; } = "route";
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
    public string? RetryConfig { get; set; }            // JSON
    public string? RateLimitConfig { get; set; }        // JSON
    public string? CircuitBreakerConfig { get; set; }   // JSON
    /// <summary>"true" / "false" / null (follow global)</summary>
    public string? WafEnabled { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
