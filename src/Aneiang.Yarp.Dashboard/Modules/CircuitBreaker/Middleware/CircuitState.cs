using Aneiang.Yarp.Dashboard.Infrastructure;

namespace Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;

/// <summary>
/// Per-destination circuit state with concurrency-safe transition tracking.
/// </summary>
public class CircuitState
{
    /// <summary>Per-circuit lock object for protecting all state transitions under concurrency.</summary>
    public readonly object SyncRoot = new();
    public string ClusterUid { get; set; } = string.Empty;
    public string ClusterKeySnapshot { get; set; } = string.Empty;
    public string DestinationUid { get; set; } = "any";
    public string DestinationKeySnapshot { get; set; } = "any";
    public CircuitStatus Status { get; set; } = CircuitStatus.Closed;
    public int ConsecutiveFailures { get; set; }
    public int FailureThreshold { get; set; }
    public TimeSpan RecoveryTimeout { get; set; }
    public int MaxHalfOpenAttempts { get; set; }
    public DateTime OpenedAt { get; set; }
    public int HalfOpenRequests { get; set; }
    public DateTime LastAccessedAt { get; set; } = DateTime.Now;

    public CircuitState(CircuitBreakerOptions? options = null)
    {
        ApplyOptions(options ?? new CircuitBreakerOptions());
    }

    /// <summary>
    /// Applies circuit breaker options to this state instance.
    /// </summary>
    public void ApplyOptions(CircuitBreakerOptions options)
    {
        FailureThreshold = options.DefaultFailureThreshold > 0 ? options.DefaultFailureThreshold : 5;
        RecoveryTimeout = TimeSpan.FromSeconds(options.DefaultRecoveryTimeoutSeconds > 0 ? options.DefaultRecoveryTimeoutSeconds : 30);
        MaxHalfOpenAttempts = options.HalfOpenMaxAttempts > 0 ? options.HalfOpenMaxAttempts : 1;
    }
}
