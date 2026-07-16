using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

internal static class DynamicYarpConfigHelpers
{
    public static RouteConfig NormalizeTransforms(RouteConfig route)
    {
        if (route.Transforms == null || route.Transforms.Count == 0) return route;

        var normalized = new List<IReadOnlyDictionary<string, string>>();
        var changed = false;
        foreach (var transform in route.Transforms)
        {
            if (transform.TryGetValue("X-Forwarded", out var xForwarded)
                && !string.IsNullOrWhiteSpace(xForwarded)
                && !string.Equals(xForwarded, "Set", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(xForwarded, "Append", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(xForwarded, "Remove", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(xForwarded, "Random", StringComparison.OrdinalIgnoreCase))
            {
                normalized.Add(new Dictionary<string, string> { ["X-Forwarded"] = "Set" });
                changed = true;
            }
            else
            {
                normalized.Add(transform);
            }
        }

        return changed ? route with { Transforms = normalized } : route;
    }

    public static IReadOnlyDictionary<string, string>? MergeRouteMetadata(
        IReadOnlyDictionary<string, string>? configMetadata,
        Dictionary<string, string> dynamicMetadata)
    {
        if ((configMetadata == null || configMetadata.Count == 0) && dynamicMetadata.Count == 0)
            return null;

        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        if (configMetadata != null)
        {
            foreach (var kv in configMetadata)
                merged[kv.Key] = kv.Value;
        }
        foreach (var kv in dynamicMetadata)
            merged[kv.Key] = kv.Value;

        return merged.Count > 0 ? merged : null;
    }

    public static string? TrySerializeRoute(RouteConfig route)
    {
        try { return Serialization.YarpJsonConfig.SerializeRoute(route); }
        catch { return null; }
    }

    public static string? TrySerializeCluster(ClusterConfig cluster)
    {
        try { return Serialization.YarpJsonConfig.SerializeCluster(cluster); }
        catch { return null; }
    }

    public static string? PatchRouteConfigJson(string? existingJson, string clusterId, string matchPath, int order, List<Dictionary<string, string>>? transforms)
    {
        if (string.IsNullOrWhiteSpace(existingJson)) return null;

        RouteConfig? parsed;
        try { parsed = Serialization.YarpJsonConfig.DeserializeRoute(existingJson); }
        catch { return null; }
        if (parsed == null) return null;

        var patched = parsed with
        {
            ClusterId = clusterId,
            Match = parsed.Match != null
                ? new RouteMatch
                {
                    Path = matchPath,
                    Hosts = parsed.Match.Hosts,
                    Methods = parsed.Match.Methods,
                    Headers = parsed.Match.Headers,
                    QueryParameters = parsed.Match.QueryParameters
                }
                : new RouteMatch { Path = matchPath },
            Order = order,
            Transforms = transforms?.Select(t => (IReadOnlyDictionary<string, string>)t).ToList() ?? parsed.Transforms
        };

        return TrySerializeRoute(patched);
    }

    public static string? PatchClusterConfigJson(string? existingJson, Dictionary<string, string> destinations, string? loadBalancingPolicy)
    {
        if (string.IsNullOrWhiteSpace(existingJson)) return null;

        ClusterConfig? parsed;
        try { parsed = Serialization.YarpJsonConfig.DeserializeCluster(existingJson); }
        catch { return null; }
        if (parsed == null) return null;

        var existingDestinations = parsed.Destinations;
        var patchedDestinations = destinations.ToDictionary(
            d => d.Key,
            d =>
            {
                if (existingDestinations != null && existingDestinations.TryGetValue(d.Key, out var existing))
                    return existing with { Address = d.Value };
                return new DestinationConfig { Address = d.Value };
            });

        var patched = parsed with
        {
            Destinations = patchedDestinations,
            LoadBalancingPolicy = loadBalancingPolicy ?? parsed.LoadBalancingPolicy
        };

        return TrySerializeCluster(patched);
    }

    public static HealthCheckConfig? BuildClusterHealthCheck(Models.HealthCheckConfig? config)
    {
        if (config == null) return null;

        return new HealthCheckConfig
        {
            Active = config.Active
                ? new ActiveHealthCheckConfig
                {
                    Enabled = true,
                    Interval = config.Interval,
                    Timeout = config.Timeout,
                    Path = config.Endpoint ?? "/health"
                }
                : null,
            Passive = config.Passive
                ? new PassiveHealthCheckConfig
                {
                    Enabled = true,
                    Policy = config.PassivePolicy ?? "ConsecutiveFailures",
                    ReactivationPeriod = config.PassiveReactivationPeriod
                }
                : null
        };
    }
}
