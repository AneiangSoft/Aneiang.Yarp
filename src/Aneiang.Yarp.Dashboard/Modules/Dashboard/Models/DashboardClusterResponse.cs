using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Models;

/// <summary>
/// Cluster response with destinations and configuration.
/// </summary>
public class DashboardClusterResponse
{
    /// <summary>Cluster identifier. Kept for compatibility; prefer clusterKey for new clients.</summary>
    [JsonPropertyName("clusterId")]
    public string ClusterId { get; set; } = string.Empty;

    /// <summary>Internal immutable cluster UID.</summary>
    [JsonPropertyName("clusterUid")]
    public string? ClusterUid { get; set; }

    /// <summary>Cluster key used as YARP ClusterId.</summary>
    [JsonPropertyName("clusterKey")]
    public string ClusterKey { get; set; } = string.Empty;

    /// <summary>Display name for UI.</summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Load balancing policy.</summary>
    [JsonPropertyName("loadBalancingPolicy")]
    public string LoadBalancingPolicy { get; set; } = string.Empty;

    /// <summary>Session affinity configuration.</summary>
    [JsonPropertyName("sessionAffinity")]
    public SessionAffinityInfo? SessionAffinity { get; set; }

    /// <summary>Health check configuration.</summary>
    [JsonPropertyName("healthCheck")]
    public HealthCheckInfo? HealthCheck { get; set; }

    /// <summary>HTTP client configuration.</summary>
    [JsonPropertyName("httpClient")]
    public HttpClientInfo? HttpClient { get; set; }

    /// <summary>HTTP request configuration.</summary>
    [JsonPropertyName("httpRequest")]
    public HttpRequestInfo? HttpRequest { get; set; }

    /// <summary>Cluster metadata.</summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>Destination list.</summary>
    [JsonPropertyName("destinations")]
    public List<DashboardDestinationResponse> Destinations { get; set; } = new();

    /// <summary>Healthy destination count.</summary>
    [JsonPropertyName("healthyCount")]
    public int HealthyCount { get; set; }

    /// <summary>Unknown health status count.</summary>
    [JsonPropertyName("unknownCount")]
    public int UnknownCount { get; set; }

    /// <summary>Unhealthy destination count.</summary>
    [JsonPropertyName("unhealthyCount")]
    public int UnhealthyCount { get; set; }

    /// <summary>Total destination count.</summary>
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    /// <summary>Configuration source: "config" | "dynamic" | "dashboard" | "auto-register".</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    /// <summary>Whether the cluster is editable.</summary>
    [JsonPropertyName("isEditable")]
    public bool IsEditable { get; set; }

    /// <summary>Circuit breaker configuration.</summary>
    [JsonPropertyName("circuitBreaker")]
    public CircuitBreakerInfo? CircuitBreaker { get; set; }
}
