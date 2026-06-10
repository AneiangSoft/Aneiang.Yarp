using System.Reflection;

namespace Aneiang.Yarp.Models;

/// <summary>
/// Gateway auto-registration options.
/// Config sources (priority high to low): code > env vars > appsettings.json.
/// </summary>
public class GatewayRegistrationOptions
{
    /// <summary>JSON config section name.</summary>
    public const string SectionName = "Gateway:Registration";

    /// <summary>Auto-enabled when <see cref="GatewayUrl"/> is set.</summary>
    public bool? Enabled { get; set; }

    /// <summary>Gateway URL, e.g. http://192.168.1.100:5000. Required field.</summary>
    public string? GatewayUrl { get; set; }

    /// <summary>Route name (default: entry assembly name).</summary>
    public string? RouteName { get; set; }

    /// <summary>Cluster name (default: same as RouteName).</summary>
    public string? ClusterName { get; set; }

    /// <summary>Match path template, e.g. /api/my-service/{**catch-all}. Default: /{**catch-all}.</summary>
    public string? MatchPath { get; set; }

    /// <summary>
    /// Destination address, e.g. http://localhost:5001.
    /// Default: auto-detected from Kestrel binding; localhost is auto-resolved to LAN IP.
    /// </summary>
    public string? DestinationAddress { get; set; }

    /// <summary>Route priority - lower = higher precedence. Default: 50.</summary>
    public int? Order { get; set; }

    /// <summary>Auto-resolve localhost/127.0.0.1/0.0.0.0 to LAN IPv4. Default: true.</summary>
    public bool? AutoResolveIp { get; set; }

    /// <summary>HTTP timeout in seconds. Default: 5.</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>Bearer token for gateway API auth.</summary>
    public string? AuthToken { get; set; }

    /// <summary>API Key for gateway API auth. Sent as X-Api-Key header.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Basic auth username. Must pair with <see cref="BasicAuthPassword"/>.</summary>
    public string? BasicAuthUsername { get; set; }

    /// <summary>Basic auth password. Must pair with <see cref="BasicAuthUsername"/>.</summary>
    public string? BasicAuthPassword { get; set; }

    /// <summary>Use gRPC registration instead of the legacy HTTP registration endpoints.</summary>
    public bool? UseGrpcRegistration { get; set; }

    /// <summary>
    /// Override downstream path prefix. If set, requests will be forwarded to this path prefix.
    /// </summary>
    public string? DownstreamPathPrefix { get; set; }

    /// <summary>
    /// Custom YARP transforms for fine-grained control. Highest priority.
    /// </summary>
    public List<Dictionary<string, string>>? Transforms { get; set; }

    /// <summary>
    /// Use IP-based instance isolation. Routes requests based on client IP address.
    /// When enabled, the gateway uses YARP's IpBased load balancing policy to automatically
    /// route requests to the correct backend instance. No path prefix is needed.
    /// Default: false.
    /// </summary>
    public bool? UseIpIsolation { get; set; }
}
