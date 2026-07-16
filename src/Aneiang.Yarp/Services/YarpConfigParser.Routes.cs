using Microsoft.Extensions.Configuration;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

partial class YarpConfigParser
{
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
                Order = child["Order"] is { Length: > 0 } o && int.TryParse(o, out var order) ? order : int.MaxValue,
                MaxRequestBodySize = child["MaxRequestBodySize"] is { Length: > 0 } s && long.TryParse(s, out var size) ? size : null,
                AuthorizationPolicy = child["AuthorizationPolicy"],
                CorsPolicy = child["CorsPolicy"],
                Metadata = metadata,
                Transforms = transforms
            });
        }
        return routes;
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

    private static string MapTransformToApi(string key, string value)
    {
        if (string.Equals(key, "X-Forwarded", StringComparison.OrdinalIgnoreCase))
        {
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
