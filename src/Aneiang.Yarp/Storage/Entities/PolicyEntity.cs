namespace Aneiang.Yarp.Storage;

/// <summary>Gateway Policy entity for database storage.</summary>
public class PolicyEntity
{
    public string PolicyId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Priority { get; set; } = 50;
    public bool Enabled { get; set; } = true;
    public string? CircuitBreakerConfig { get; set; } // JSON
    public string? RetryConfig { get; set; } // JSON
    public string? RateLimitConfig { get; set; } // JSON
    public string? WafConfig { get; set; } // JSON
    public string? CustomPlugins { get; set; } // JSON
    public string? Tags { get; set; } // JSON array
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
