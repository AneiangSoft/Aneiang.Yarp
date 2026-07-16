using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Models;

/// <summary>
/// Circuit breaker configuration.
/// </summary>
public class CircuitBreakerInfo
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("failureThreshold")]
    public int FailureThreshold { get; set; }

    [JsonPropertyName("recoveryTimeoutSeconds")]
    public int RecoveryTimeoutSeconds { get; set; }

    [JsonPropertyName("halfOpenMaxAttempts")]
    public int HalfOpenMaxAttempts { get; set; }

    [JsonPropertyName("failureStatusCodes")]
    public List<int>? FailureStatusCodes { get; set; }
}
