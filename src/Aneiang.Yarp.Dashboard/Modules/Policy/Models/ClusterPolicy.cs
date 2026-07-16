namespace Aneiang.Yarp.Dashboard.Modules.Policy.Models;

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
    public DateTime CreatedAt { get; set; } = DateTime.Now;

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
