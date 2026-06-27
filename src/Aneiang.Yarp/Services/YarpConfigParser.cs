using System.Security.Authentication;
using Microsoft.Extensions.Configuration;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace Aneiang.Yarp.Services;

/// <summary>Static helper for parsing YARP routes, clusters, and transforms from IConfiguration.</summary>
internal static class YarpConfigParser
{
    /// <summary>Parse routes from configuration section.</summary>
    public static List<RouteConfig> ParseRoutes(IConfigurationSection section)
    {
        var routes = new List<RouteConfig>();
        foreach (var child in section.GetChildren())
        {
            var transforms = ParseTransforms(child.GetSection("Transforms"));
            var metadata = ParseMetadata(child.GetSection("Metadata"));
            routes.Add(new RouteConfig
            {
                RouteId = child.Key,
                ClusterId = child["ClusterId"]!,
                Match = ParseMatch(child.GetSection("Match"))!,
                Order = child["Order"] is { Length: > 0 } o && int.TryParse(o, out var order) ? order : null,
                MaxRequestBodySize = child["MaxRequestBodySize"] is { Length: > 0 } s && long.TryParse(s, out var size) ? size : null,
                AuthorizationPolicy = child["AuthorizationPolicy"],
                CorsPolicy = child["CorsPolicy"],
                Metadata = metadata,
                Transforms = transforms
            });
        }
        return routes;
    }

    /// <summary>Parse clusters from configuration section.</summary>
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
                    ReactivationPeriod = passive["ReactivationPeriod"] is { Length: > 0 } rp && TimeSpan.TryParse(rp, out var rpT) ? rpT : null
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
            DangerousAcceptAnyServerCertificate = section["DangerousAcceptAnyServerCertificate"] is { Length: > 0 } d && bool.TryParse(d, out var dv) ? dv : null,
            MaxConnectionsPerServer = section["MaxConnectionsPerServer"] is { Length: > 0 } m && int.TryParse(m, out var mv) ? mv : null,
            EnableMultipleHttp2Connections = section["EnableMultipleHttp2Connections"] is { Length: > 0 } me && bool.TryParse(me, out var mev) ? mev : null,
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
            BypassOnLocal = section["BypassOnLocal"] is { Length: > 0 } b && bool.TryParse(b, out var bv) ? bv : null,
            UseDefaultCredentials = section["UseDefaultCredentials"] is { Length: > 0 } u && bool.TryParse(u, out var uv) ? uv : null
        };
    }

    private static ForwarderRequestConfig? ParseForwarderRequestConfig(IConfigurationSection section)
    {
        if (!section.Exists()) return null;
        return new ForwarderRequestConfig
        {
            ActivityTimeout = section["ActivityTimeout"] is { Length: > 0 } a && TimeSpan.TryParse(a, out var at) ? at : null,
            Version = section["Version"] is { Length: > 0 } ve && System.Version.TryParse(ve, out var vv) ? vv : null,
            VersionPolicy = section["VersionPolicy"] is { Length: > 0 } vp
                ? Enum.TryParse<System.Net.Http.HttpVersionPolicy>(vp, ignoreCase: true, out var vpe) ? vpe : null
                : null,
            AllowResponseBuffering = section["AllowResponseBuffering"] is { Length: > 0 } ar && bool.TryParse(ar, out var arv) ? arv : null
        };
    }

    private static RouteMatch? ParseMatch(IConfigurationSection section)
    {
        if (!section.Exists()) return null;

        // Short form: "Path": "/api/{**catch-all}"
        var path = section.Value;
        if (!string.IsNullOrEmpty(path))
            return new RouteMatch { Path = path };

        // Full form: Path, Methods, Hosts, Headers, QueryParameters
        var methods = section.GetSection("Methods").GetChildren()
            .Select(m => m.Value).Where(v => v != null).Cast<string>().ToArray();

        var hosts = section.GetSection("Hosts").GetChildren()
            .Select(h => h.Value).Where(v => v != null).Cast<string>().ToArray();

        var headers = ParseRouteHeaders(section.GetSection("Headers"));
        var queryParameters = ParseRouteQueryParameters(section.GetSection("QueryParameters"));

        return new RouteMatch
        {
            Path = section["Path"],
            Methods = methods.Length > 0 ? methods : null,
            Hosts = hosts.Length > 0 ? hosts : null,
            Headers = headers,
            QueryParameters = queryParameters
        };
    }

    private static IReadOnlyList<RouteHeader>? ParseRouteHeaders(IConfigurationSection section)
    {
        if (!section.Exists()) return null;
        var list = new List<RouteHeader>();
        foreach (var child in section.GetChildren())
        {
            var values = child.GetSection("Values").GetChildren()
                .Select(v => v.Value).Where(v => v != null).Cast<string>().ToArray();

            list.Add(new RouteHeader
            {
                Name = child["Name"]!,
                Values = values.Length > 0 ? values : null,
                Mode = child["Mode"] is { Length: > 0 } m
                    ? Enum.TryParse<HeaderMatchMode>(m, ignoreCase: true, out var mode) ? mode : HeaderMatchMode.ExactHeader
                    : HeaderMatchMode.ExactHeader,
                IsCaseSensitive = child["IsCaseSensitive"] is { Length: > 0 } cs && bool.TryParse(cs, out var b) ? b : false
            });
        }
        return list.Count > 0 ? list : null;
    }

    private static IReadOnlyList<RouteQueryParameter>? ParseRouteQueryParameters(IConfigurationSection section)
    {
        if (!section.Exists()) return null;
        var list = new List<RouteQueryParameter>();
        foreach (var child in section.GetChildren())
        {
            var values = child.GetSection("Values").GetChildren()
                .Select(v => v.Value).Where(v => v != null).Cast<string>().ToArray();

            list.Add(new RouteQueryParameter
            {
                Name = child["Name"]!,
                Values = values.Length > 0 ? values : null,
                Mode = child["Mode"] is { Length: > 0 } m
                    ? Enum.TryParse<QueryParameterMatchMode>(m, ignoreCase: true, out var mode) ? mode : QueryParameterMatchMode.Exact
                    : QueryParameterMatchMode.Exact,
                IsCaseSensitive = child["IsCaseSensitive"] is { Length: > 0 } cs && bool.TryParse(cs, out var b) ? b : false
            });
        }
        return list.Count > 0 ? list : null;
    }

    private static Dictionary<string, string>? ParseMetadata(IConfigurationSection section)
    {
        if (!section.Exists()) return null;
        var dict = new Dictionary<string, string>();
        foreach (var child in section.GetChildren())
            if (child.Value != null) dict[child.Key] = child.Value;
        return dict.Count > 0 ? dict : null;
    }

    /// <summary>
    /// Transform keys whose appsettings.json syntax (e.g. X-Forwarded: For,Proto,Host,Prefix)
    /// must be converted to the API/InMemoryConfigProvider enum format (e.g. X-Forwarded: Set).
    /// </summary>
    private static readonly HashSet<string> _apiCompatibleTransformKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "X-Forwarded"
    };

    private static List<Dictionary<string, string>>? ParseTransforms(IConfigurationSection section)
    {
        if (!section.Exists()) return null;

        var list = new List<Dictionary<string, string>>();
        foreach (var t in section.GetChildren())
        {
            var dict = new Dictionary<string, string>();
            foreach (var entry in t.GetChildren())
            {
                if (entry.Value == null) continue;

                if (_apiCompatibleTransformKeys.Contains(entry.Key))
                {
                    dict[entry.Key] = MapTransformToApi(entry.Key, entry.Value);
                }
                else
                {
                    dict[entry.Key] = entry.Value;
                }
            }
            if (dict.Count > 0) list.Add(dict);
        }
        return list.Count > 0 ? list : null;
    }

    /// <summary>
    /// Convert appsettings.json-style descriptive transform values to the API/InMemory enum values
    /// that YARP's InMemoryConfigProvider accepts. For example, X-Forwarded: For,Proto,Host,Prefix → Set.
    /// </summary>
    private static string MapTransformToApi(string key, string value)
    {
        if (string.Equals(key, "X-Forwarded", StringComparison.OrdinalIgnoreCase))
        {
            // In appsettings.json, "For,Proto,Host,Prefix" selects which X-Forwarded-* headers to set.
            // In API mode, the enum ForwardedTransformActions uses Set/Append/Remove/Random.
            // Any non-empty descriptive value → Set (which sets all standard X-Forwarded headers).
            if (!string.IsNullOrWhiteSpace(value) &&
                !string.Equals(value, "Set", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(value, "Append", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(value, "Remove", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(value, "Random", StringComparison.OrdinalIgnoreCase))
            {
                return "Set";
            }
        }
        return value;
    }
}
