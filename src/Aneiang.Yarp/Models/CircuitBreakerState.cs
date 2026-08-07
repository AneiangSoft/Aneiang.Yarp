namespace Aneiang.Yarp.Models;

public enum CircuitStatus { Closed, Open, HalfOpen }

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
    public Queue<(DateTime Timestamp, bool Failed)> Samples { get; } = new();
    public int SampleFailures { get; set; }
    public double FailureRatio { get; set; } = 0.5;
    public int MinimumThroughput { get; set; } = 10;
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(30);
    public DateTime LastAccessedAt { get; set; } = DateTime.Now;

    public CircuitState(CircuitBreakerConfig? config = null)
    {
        ApplyConfig(config ?? new CircuitBreakerConfig());
    }

    public void ApplyConfig(CircuitBreakerConfig config)
    {
        FailureThreshold = config.FailureThreshold > 0 ? config.FailureThreshold : 5;
        RecoveryTimeout = TimeSpan.FromSeconds(config.RecoveryTimeoutSeconds > 0 ? config.RecoveryTimeoutSeconds : 30);
        MaxHalfOpenAttempts = config.HalfOpenMaxAttempts > 0 ? config.HalfOpenMaxAttempts : 1;
        FailureRatio = config.FailureRatio is > 0 and <= 1 ? config.FailureRatio : 0.5;
        MinimumThroughput = config.MinimumThroughput > 0 ? config.MinimumThroughput : 10;
        SamplingDuration = TimeSpan.FromSeconds(config.SamplingDurationSeconds > 0 ? config.SamplingDurationSeconds : 30);
    }
}

public class CircuitStateInfo
{
    public string Key { get; set; } = string.Empty;
    public string ClusterUid { get; set; } = string.Empty;
    public string ClusterKeySnapshot { get; set; } = string.Empty;
    public string ClusterName { get; set; } = string.Empty;
    public string DestinationUid { get; set; } = "any";
    public string DestinationKeySnapshot { get; set; } = "any";
    public string Status { get; set; } = "Closed";
    public int ConsecutiveFailures { get; set; }
    public int FailureThreshold { get; set; }
    public TimeSpan RecoveryTimeout { get; set; }
    public int RecoveryTimeoutSeconds { get; set; }
    public int HalfOpenRequests { get; set; }
    public int MaxHalfOpenAttempts { get; set; }
    public DateTime? OpenedAt { get; set; }
    public DateTime LastAccessedAt { get; set; }
}

/// <summary>Internal action determined by circuit breaker lock evaluation.</summary>
public enum CircuitAction { Proceed, RejectOpen, RejectHalfOpen }
