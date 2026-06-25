using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Models;

/// <summary>
/// Route response with match criteria and transforms.
/// </summary>
public class DashboardRouteResponse
{
    /// <summary>Route identifier. Kept for compatibility; prefer routeKey for new clients.</summary>
    [JsonPropertyName("routeId")]
    public string RouteId { get; set; } = string.Empty;

    /// <summary>Internal immutable route UID.</summary>
    [JsonPropertyName("routeUid")]
    public string? RouteUid { get; set; }

    /// <summary>Route key used as YARP RouteId.</summary>
    [JsonPropertyName("routeKey")]
    public string RouteKey { get; set; } = string.Empty;

    /// <summary>Display name for UI.</summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Internal immutable cluster UID.</summary>
    [JsonPropertyName("clusterUid")]
    public string? ClusterUid { get; set; }

    /// <summary>Cluster key used as YARP ClusterId.</summary>
    [JsonPropertyName("clusterKey")]
    public string? ClusterKey { get; set; }

    /// <summary>Cluster identifier.</summary>
    [JsonPropertyName("clusterId")]
    public string? ClusterId { get; set; }

    /// <summary>Route match criteria (standard YARP structure).</summary>
    [JsonPropertyName("match")]
    public RouteMatchInfo? Match { get; set; }

    /// <summary>Route order.</summary>
    [JsonPropertyName("order")]
    public int? Order { get; set; }

    /// <summary>Authorization policy.</summary>
    [JsonPropertyName("authorizationPolicy")]
    public string? AuthorizationPolicy { get; set; }

    /// <summary>CORS policy.</summary>
    [JsonPropertyName("corsPolicy")]
    public string? CorsPolicy { get; set; }

    /// <summary>Output cache policy.</summary>
    [JsonPropertyName("outputCachePolicy")]
    public string? OutputCachePolicy { get; set; }

    /// <summary>Max request body size.</summary>
    [JsonPropertyName("maxRequestBodySize")]
    public long? MaxRequestBodySize { get; set; }

    /// <summary>Destination list.</summary>
    [JsonPropertyName("destinations")]
    public List<RouteDestinationInfo>? Destinations { get; set; }

    /// <summary>Transform configurations.</summary>
    [JsonPropertyName("transforms")]
    public List<Dictionary<string, string>>? Transforms { get; set; }

    /// <summary>Rate limiter policy.</summary>
    [JsonPropertyName("rateLimiterPolicy")]
    public string? RateLimiterPolicy { get; set; }

    /// <summary>Timeout policy.</summary>
    [JsonPropertyName("timeoutPolicy")]
    public string? TimeoutPolicy { get; set; }

    /// <summary>Timeout duration.</summary>
    [JsonPropertyName("timeout")]
    public string? Timeout { get; set; }

    /// <summary>Route metadata.</summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>Configuration source: "config" | "dynamic" | "dashboard" | "auto-register".</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    /// <summary>Whether the route is editable.</summary>
    [JsonPropertyName("isEditable")]
    public bool IsEditable { get; set; }
}

/// <summary>
/// Route match criteria (standard YARP structure).
/// </summary>
public class RouteMatchInfo
{
    /// <summary>Match path pattern.</summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    /// <summary>Allowed HTTP methods.</summary>
    [JsonPropertyName("methods")]
    public IReadOnlyList<string>? Methods { get; set; }

    /// <summary>Allowed hosts.</summary>
    [JsonPropertyName("hosts")]
    public IReadOnlyList<string>? Hosts { get; set; }

    /// <summary>Header match criteria.</summary>
    [JsonPropertyName("headers")]
    public List<RouteHeaderInfo>? Headers { get; set; }

    /// <summary>Query parameter match criteria.</summary>
    [JsonPropertyName("queryParameters")]
    public List<RouteQueryParameterInfo>? QueryParameters { get; set; }
}

/// <summary>
/// Route destination information.
/// </summary>
public class RouteDestinationInfo
{
    /// <summary>Destination name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Destination address.</summary>
    [JsonPropertyName("address")]
    public string? Address { get; set; }
}

/// <summary>
/// Route header match information.
/// </summary>
public class RouteHeaderInfo
{
    /// <summary>Header name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Header values to match.</summary>
    [JsonPropertyName("values")]
    public IReadOnlyList<string>? Values { get; set; }

    /// <summary>Match mode.</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = string.Empty;

    /// <summary>Whether value matching is case-sensitive.</summary>
    [JsonPropertyName("isCaseSensitive")]
    public bool IsCaseSensitive { get; set; }
}

/// <summary>
/// Route query parameter match information.
/// </summary>
public class RouteQueryParameterInfo
{
    /// <summary>Query parameter name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Query parameter values to match.</summary>
    [JsonPropertyName("values")]
    public IReadOnlyList<string>? Values { get; set; }

    /// <summary>Match mode.</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = string.Empty;

    /// <summary>Whether value matching is case-sensitive.</summary>
    [JsonPropertyName("isCaseSensitive")]
    public bool IsCaseSensitive { get; set; }
}
