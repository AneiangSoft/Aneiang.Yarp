namespace Aneiang.Yarp.Models;

/// <summary>
/// Dynamic route configuration with metadata for persistence.
/// </summary>
public class DynamicRouteConfig
{
    /// <summary>Internal immutable route UID.</summary>
    public string RouteUid { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Route key used as YARP RouteId.</summary>
    public string RouteId { get; set; } = string.Empty;

    /// <summary>Route key alias. Prefer this for internal semantics; RouteId is kept for compatibility.</summary>
    public string RouteKey
    {
        get => RouteId;
        set => RouteId = value;
    }

    /// <summary>Display name for UI. Defaults to RouteKey when empty.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Internal immutable cluster UID this route belongs to.</summary>
    public string? ClusterUid { get; set; }

    /// <summary>Cluster key this route belongs to. Used as YARP ClusterId.</summary>
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
