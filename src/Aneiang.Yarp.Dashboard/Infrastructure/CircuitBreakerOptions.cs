namespace Aneiang.Yarp.Dashboard.Infrastructure;

/// <summary>
/// Circuit breaker configuration options.
/// Can be configured globally in DashboardOptions or per-route via metadata.
/// </summary>
public class CircuitBreakerOptions
{
    /// <summary>Enable circuit breaker for proxy routes. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Number of consecutive failures before opening the circuit.
    /// Route metadata key: CircuitBreaker:FailureThreshold. Default: 5.
    /// </summary>
    public int DefaultFailureThreshold { get; set; } = 5;

    /// <summary>
    /// Seconds to wait before transitioning from Open to HalfOpen.
    /// Route metadata key: CircuitBreaker:RecoveryTimeoutSeconds. Default: 30.
    /// </summary>
    public int DefaultRecoveryTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum number of requests allowed in HalfOpen state before deciding circuit state.
    /// Route metadata key: CircuitBreaker:HalfOpenMaxAttempts. Default: 1.
    /// </summary>
    public int HalfOpenMaxAttempts { get; set; } = 1;

    /// <summary>
    /// Maximum number of circuit breaker entries to track.
    /// Prevents memory exhaustion from too many unique circuit keys.
    /// Default: 1000.
    /// </summary>
    public int MaxCircuitCount { get; set; } = 1000;
}
