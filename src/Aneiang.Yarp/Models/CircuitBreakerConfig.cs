namespace Aneiang.Yarp.Models;

/// <summary>
/// Circuit breaker configuration at cluster level.
/// </summary>
public class CircuitBreakerConfig
{
    /// <summary>Enable circuit breaker. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Consecutive failures before opening circuit. Default: 5.</summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>Seconds before attempting recovery. Default: 30.</summary>
    public int RecoveryTimeoutSeconds { get; set; } = 30;

    /// <summary>Max requests allowed in half-open state. Default: 1.</summary>
    public int HalfOpenMaxAttempts { get; set; } = 1;

    /// <summary>Failure ratio required to open the circuit in the sampling window.</summary>
    public double FailureRatio { get; set; } = 0.5;

    /// <summary>Minimum sampled requests before evaluating the failure ratio.</summary>
    public int MinimumThroughput { get; set; } = 10;

    /// <summary>Rolling sampling window in seconds.</summary>
    public int SamplingDurationSeconds { get; set; } = 30;

    /// <summary>HTTP status codes that count as failures.</summary>
    public List<int> FailureStatusCodes { get; set; } = new() { 500, 502, 503, 504 };
}
