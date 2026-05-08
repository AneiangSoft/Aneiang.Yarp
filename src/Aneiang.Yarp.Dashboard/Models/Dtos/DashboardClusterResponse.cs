using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Models.Dtos;

/// <summary>
/// Cluster response with destinations and configuration.
/// </summary>
public class DashboardClusterResponse
{
    /// <summary>Cluster identifier.</summary>
    [JsonPropertyName("clusterId")]
    public string ClusterId { get; set; } = string.Empty;

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
}

/// <summary>
/// Destination response with health status.
/// </summary>
public class DashboardDestinationResponse
{
    /// <summary>Destination name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Destination address.</summary>
    [JsonPropertyName("address")]
    public string? Address { get; set; }

    /// <summary>Destination health.</summary>
    [JsonPropertyName("health")]
    public string? Health { get; set; }

    /// <summary>Destination host.</summary>
    [JsonPropertyName("host")]
    public string? Host { get; set; }

    /// <summary>Destination metadata.</summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>Active health status.</summary>
    [JsonPropertyName("activeHealth")]
    public string ActiveHealth { get; set; } = string.Empty;

    /// <summary>Passive health status.</summary>
    [JsonPropertyName("passiveHealth")]
    public string PassiveHealth { get; set; } = string.Empty;
}

/// <summary>
/// Session affinity configuration.
/// </summary>
public class SessionAffinityInfo
{
    /// <summary>Whether session affinity is enabled.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>Affinity policy.</summary>
    [JsonPropertyName("policy")]
    public string? Policy { get; set; }

    /// <summary>Failure policy.</summary>
    [JsonPropertyName("failurePolicy")]
    public string? FailurePolicy { get; set; }

    /// <summary>Affinity key name.</summary>
    [JsonPropertyName("affinityKeyName")]
    public string? AffinityKeyName { get; set; }

    /// <summary>Cookie configuration.</summary>
    [JsonPropertyName("cookie")]
    public SessionAffinityCookieInfo? Cookie { get; set; }
}

/// <summary>
/// Session affinity cookie configuration.
/// </summary>
public class SessionAffinityCookieInfo
{
    /// <summary>Cookie domain.</summary>
    [JsonPropertyName("domain")]
    public string? Domain { get; set; }

    /// <summary>Cookie path.</summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    /// <summary>Cookie expiration.</summary>
    [JsonPropertyName("expiration")]
    public string? Expiration { get; set; }

    /// <summary>Cookie max age.</summary>
    [JsonPropertyName("maxAge")]
    public string? MaxAge { get; set; }

    /// <summary>Cookie secure policy.</summary>
    [JsonPropertyName("securePolicy")]
    public string? SecurePolicy { get; set; }

    /// <summary>Whether cookie is HTTP only.</summary>
    [JsonPropertyName("httpOnly")]
    public bool HttpOnly { get; set; }

    /// <summary>Cookie same site policy.</summary>
    [JsonPropertyName("sameSite")]
    public string? SameSite { get; set; }

    /// <summary>Whether cookie is essential.</summary>
    [JsonPropertyName("isEssential")]
    public bool IsEssential { get; set; }
}

/// <summary>
/// Health check configuration.
/// </summary>
public class HealthCheckInfo
{
    /// <summary>Active health check configuration.</summary>
    [JsonPropertyName("active")]
    public ActiveHealthCheckInfo? Active { get; set; }

    /// <summary>Passive health check configuration.</summary>
    [JsonPropertyName("passive")]
    public PassiveHealthCheckInfo? Passive { get; set; }

    /// <summary>Available destinations policy.</summary>
    [JsonPropertyName("availableDestinationsPolicy")]
    public string? AvailableDestinationsPolicy { get; set; }
}

/// <summary>
/// Active health check configuration.
/// </summary>
public class ActiveHealthCheckInfo
{
    /// <summary>Whether active health check is enabled.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>Check interval.</summary>
    [JsonPropertyName("interval")]
    public string? Interval { get; set; }

    /// <summary>Check timeout.</summary>
    [JsonPropertyName("timeout")]
    public string? Timeout { get; set; }

    /// <summary>Health check policy.</summary>
    [JsonPropertyName("policy")]
    public string? Policy { get; set; }

    /// <summary>Health check path.</summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    /// <summary>Health check query.</summary>
    [JsonPropertyName("query")]
    public string? Query { get; set; }
}

/// <summary>
/// Passive health check configuration.
/// </summary>
public class PassiveHealthCheckInfo
{
    /// <summary>Whether passive health check is enabled.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>Health check policy.</summary>
    [JsonPropertyName("policy")]
    public string? Policy { get; set; }

    /// <summary>Reactivation period.</summary>
    [JsonPropertyName("reactivationPeriod")]
    public string? ReactivationPeriod { get; set; }
}

/// <summary>
/// HTTP client configuration.
/// </summary>
public class HttpClientInfo
{
    /// <summary>SSL protocols.</summary>
    [JsonPropertyName("sslProtocols")]
    public string? SslProtocols { get; set; }

    /// <summary>Whether to accept any server certificate.</summary>
    [JsonPropertyName("dangerousAcceptAnyServerCertificate")]
    public bool DangerousAcceptAnyServerCertificate { get; set; }

    /// <summary>Max connections per server.</summary>
    [JsonPropertyName("maxConnectionsPerServer")]
    public int? MaxConnectionsPerServer { get; set; }

    /// <summary>Whether to enable multiple HTTP/2 connections.</summary>
    [JsonPropertyName("enableMultipleHttp2Connections")]
    public bool EnableMultipleHttp2Connections { get; set; }

    /// <summary>Request header encoding.</summary>
    [JsonPropertyName("requestHeaderEncoding")]
    public string? RequestHeaderEncoding { get; set; }

    /// <summary>Response header encoding.</summary>
    [JsonPropertyName("responseHeaderEncoding")]
    public string? ResponseHeaderEncoding { get; set; }

    /// <summary>Web proxy configuration.</summary>
    [JsonPropertyName("webProxy")]
    public WebProxyInfo? WebProxy { get; set; }
}

/// <summary>
/// Web proxy configuration.
/// </summary>
public class WebProxyInfo
{
    /// <summary>Proxy address.</summary>
    [JsonPropertyName("address")]
    public string? Address { get; set; }

    /// <summary>Whether to bypass proxy on local addresses.</summary>
    [JsonPropertyName("bypassOnLocal")]
    public bool BypassOnLocal { get; set; }

    /// <summary>Whether to use default credentials.</summary>
    [JsonPropertyName("useDefaultCredentials")]
    public bool UseDefaultCredentials { get; set; }
}

/// <summary>
/// HTTP request configuration.
/// </summary>
public class HttpRequestInfo
{
    /// <summary>Activity timeout.</summary>
    [JsonPropertyName("activityTimeout")]
    public string? ActivityTimeout { get; set; }

    /// <summary>HTTP version.</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>Version policy.</summary>
    [JsonPropertyName("versionPolicy")]
    public string? VersionPolicy { get; set; }

    /// <summary>Whether to allow response buffering.</summary>
    [JsonPropertyName("allowResponseBuffering")]
    public bool? AllowResponseBuffering { get; set; }
}
