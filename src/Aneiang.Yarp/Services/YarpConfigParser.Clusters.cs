using System.Security.Authentication;
using Microsoft.Extensions.Configuration;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace Aneiang.Yarp.Services;

partial class YarpConfigParser
{
    public static List<ClusterConfig> ParseClusters(IConfigurationSection section)
    {
        var clusters = new List<ClusterConfig>();
        foreach (var child in section.GetChildren())
        {
            var destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (var dest in child.GetSection("Destinations").GetChildren())
            {
                destinations[dest.Key] = new DestinationConfig
                {
                    Address = dest["Address"]!,
                    Health = dest["Health"],
                    Host = dest["Host"],
                    Metadata = ParseMetadata(dest.GetSection("Metadata"))
                };
            }

            var clusterMetadata = ParseMetadata(child.GetSection("Metadata"));

            clusters.Add(new ClusterConfig
            {
                ClusterId = child.Key,
                Destinations = destinations,
                LoadBalancingPolicy = child["LoadBalancingPolicy"],
                SessionAffinity = ParseSessionAffinity(child.GetSection("SessionAffinity")),
                HealthCheck = ParseHealthCheck(child.GetSection("HealthCheck")),
                HttpClient = ParseHttpClientConfig(child.GetSection("HttpClient")),
                HttpRequest = ParseForwarderRequestConfig(child.GetSection("HttpRequest")),
                Metadata = clusterMetadata
            });
        }
        return clusters;
    }

    private static SessionAffinityConfig? ParseSessionAffinity(IConfigurationSection section)
    {
        if (!section.Exists()) return null;
        return new SessionAffinityConfig
        {
            Enabled = bool.TryParse(section["Enabled"], out var enabled) && enabled,
            Policy = section["Policy"],
            FailurePolicy = section["FailurePolicy"],
            AffinityKeyName = section["AffinityKeyName"] ?? ".Yarp.ReverseProxy.Affinity"
        };
    }

    private static global::Yarp.ReverseProxy.Configuration.HealthCheckConfig? ParseHealthCheck(IConfigurationSection section)
    {
        if (!section.Exists()) return null;
        var active = section.GetSection("Active");
        var passive = section.GetSection("Passive");
        return new global::Yarp.ReverseProxy.Configuration.HealthCheckConfig
        {
            Active = active.Exists()
                ? new ActiveHealthCheckConfig
                {
                    Enabled = bool.TryParse(active["Enabled"], out var aen) && aen,
                    Interval = active["Interval"] is { Length: > 0 } i && TimeSpan.TryParse(i, out var itv) ? itv : null,
                    Timeout = active["Timeout"] is { Length: > 0 } t && TimeSpan.TryParse(t, out var to) ? to : null,
                    Policy = active["Policy"],
                    Path = active["Path"],
                    Query = active["Query"]
                }
                : null,
            Passive = passive.Exists()
                ? new PassiveHealthCheckConfig
                {
                    Enabled = bool.TryParse(passive["Enabled"], out var pen) && pen,
                    Policy = passive["Policy"],
                    ReactivationPeriod = passive["ReactivationPeriod"] is { Length: > 0 } rp
                        && TimeSpan.TryParse(rp, out var rpT) ? rpT : null
                }
                : null,
            AvailableDestinationsPolicy = section["AvailableDestinationsPolicy"]
        };
    }

    private static HttpClientConfig? ParseHttpClientConfig(IConfigurationSection section)
    {
        if (!section.Exists()) return null;
        return new HttpClientConfig
        {
            SslProtocols = section["SslProtocols"] is { Length: > 0 } sp
                && Enum.TryParse<SslProtocols>(sp, ignoreCase: true, out var sv) ? sv : null,
            DangerousAcceptAnyServerCertificate = section["DangerousAcceptAnyServerCertificate"] is { Length: > 0 } d
                && bool.TryParse(d, out var dv) ? dv : null,
            MaxConnectionsPerServer = section["MaxConnectionsPerServer"] is { Length: > 0 } m
                && int.TryParse(m, out var mv) ? mv : null,
            EnableMultipleHttp2Connections = section["EnableMultipleHttp2Connections"] is { Length: > 0 } me
                && bool.TryParse(me, out var mev) ? mev : null,
            RequestHeaderEncoding = section["RequestHeaderEncoding"],
            ResponseHeaderEncoding = section["ResponseHeaderEncoding"],
            WebProxy = ParseWebProxyConfig(section.GetSection("WebProxy"))
        };
    }

    private static WebProxyConfig? ParseWebProxyConfig(IConfigurationSection section)
    {
        if (!section.Exists()) return null;
        var address = section["Address"];
        return new WebProxyConfig
        {
            Address = !string.IsNullOrWhiteSpace(address) ? new Uri(address) : null,
            BypassOnLocal = section["BypassOnLocal"] is { Length: > 0 } b
                && bool.TryParse(b, out var bv) ? bv : null,
            UseDefaultCredentials = section["UseDefaultCredentials"] is { Length: > 0 } u
                && bool.TryParse(u, out var uv) ? uv : null
        };
    }

    private static ForwarderRequestConfig? ParseForwarderRequestConfig(IConfigurationSection section)
    {
        if (!section.Exists()) return null;
        return new ForwarderRequestConfig
        {
            ActivityTimeout = section["ActivityTimeout"] is { Length: > 0 } a
                && TimeSpan.TryParse(a, out var at) ? at : null,
            Version = section["Version"] is { Length: > 0 } ve
                && System.Version.TryParse(ve, out var vv) ? vv : null,
            VersionPolicy = section["VersionPolicy"] is { Length: > 0 } vp
                ? Enum.TryParse<System.Net.Http.HttpVersionPolicy>(vp, ignoreCase: true, out var vpe) ? vpe : null
                : null,
            AllowResponseBuffering = section["AllowResponseBuffering"] is { Length: > 0 } ar
                && bool.TryParse(ar, out var arv) ? arv : null
        };
    }
}
