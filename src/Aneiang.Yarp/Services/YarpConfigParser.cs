using Microsoft.Extensions.Configuration;
using Yarp.ReverseProxy.Configuration;

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
            routes.Add(new RouteConfig
            {
                RouteId = child.Key,
                ClusterId = child["ClusterId"]!,
                Match = ParseMatch(child.GetSection("Match"))!,
                Order = child["Order"] is { Length: > 0 } o && int.TryParse(o, out var order) ? order : null,
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
                destinations[dest.Key] = new DestinationConfig { Address = dest["Address"]! };

            clusters.Add(new ClusterConfig
            {
                ClusterId = child.Key,
                Destinations = destinations,
                LoadBalancingPolicy = child["LoadBalancingPolicy"]
            });
        }
        return clusters;
    }

    private static RouteMatch? ParseMatch(IConfigurationSection section)
    {
        if (!section.Exists()) return null;

        // Short form: "Path": "/api/{**catch-all}"
        var path = section.Value;
        if (!string.IsNullOrEmpty(path))
            return new RouteMatch { Path = path };

        // Full form: { "Path": "...", "Methods": ["GET", "POST"] }
        var methods = section.GetSection("Methods").GetChildren()
            .Select(m => m.Value).Where(v => v != null).Cast<string>().ToArray();

        return new RouteMatch
        {
            Path = section["Path"],
            Methods = methods.Length > 0 ? methods : null
        };
    }

    private static List<Dictionary<string, string>>? ParseTransforms(IConfigurationSection section)
    {
        if (!section.Exists()) return null;

        var list = new List<Dictionary<string, string>>();
        foreach (var t in section.GetChildren())
        {
            var dict = new Dictionary<string, string>();
            foreach (var entry in t.GetChildren())
                if (entry.Value != null) dict[entry.Key] = entry.Value;
            if (dict.Count > 0) list.Add(dict);
        }
        return list.Count > 0 ? list : null;
    }
}
