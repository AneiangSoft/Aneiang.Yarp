namespace Aneiang.Yarp.Models;

/// <summary>
/// Dynamic route configuration with metadata for persistence.
/// </summary>
public class DynamicRouteConfig
{
    /// <summary>Route ID (unique identifier).</summary>
    public string RouteId { get; set; } = string.Empty;

    /// <summary>Cluster ID this route belongs to.</summary>
    public string ClusterId { get; set; } = string.Empty;

    /// <summary>Route match path pattern.</summary>
    public string MatchPath { get; set; } = string.Empty;

    /// <summary>Route order (lower = higher priority).</summary>
    public int Order { get; set; } = 50;

    /// <summary>Route transforms (path rewriting, header manipulation, etc.).</summary>
    public List<Dictionary<string, string>>? Transforms { get; set; }

    /// <summary>Configuration source: "config" | "dynamic" | "auto-register".</summary>
    public string Source { get; set; } = "dynamic";

    /// <summary>When this route was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Who created this route (user name or "auto").</summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// Route metadata for extensibility (e.g., circuit breaker, retry, rate-limit, WAF policy keys).
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}
