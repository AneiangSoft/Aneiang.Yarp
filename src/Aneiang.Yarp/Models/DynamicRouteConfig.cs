using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Models;

/// <summary>
/// Dynamic route configuration: holds the complete native YARP <see cref="RouteConfig"/>
/// plus extension metadata that YARP itself does not track (UID, source, audit info, policy keys).
/// The native config is the single source of truth for all YARP fields, so nothing is lost
/// when new YARP properties are introduced.
/// </summary>
public sealed class DynamicRouteConfig
{
    /// <summary>
    /// Complete native YARP <see cref="RouteConfig"/>. Carries all fields including advanced
    /// properties (full Match criteria, Auth/Cors/RateLimiter/Timeout policies, MaxRequestBodySize, etc.).
    /// </summary>
    public RouteConfig Config { get; set; } = new() { RouteId = string.Empty, ClusterId = string.Empty };

    /// <summary>Internal immutable route UID.</summary>
    public string RouteUid { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Display name for UI. Defaults to RouteKey when empty.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Internal immutable cluster UID this route belongs to.</summary>
    public string? ClusterUid { get; set; }

    /// <summary>Configuration source: "config" | "dynamic" | "auto-register".</summary>
    public string Source { get; set; } = "dynamic";

    /// <summary>When this route was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Who created this route (user name or "auto").</summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// Whether this route is active for forwarding. When false, the route configuration
    /// is retained but excluded from the YARP snapshot so no traffic is forwarded.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
