using System.Text.Json;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;

/// <summary>
/// Service for computing configuration diffs between snapshot versions and live configuration.
/// Extracted from ConfigManagementController to keep controllers lean.
/// </summary>
public class ConfigDiffService : IConfigDiffService
{
    /// <inheritdoc />
    public string ResolveHistoryChangeType(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return "manual";
        var text = description.ToLowerInvariant();
        if (text.Contains("rollback")) return "rollback";
        if (text.Contains("import")) return "import";
        if (text.Contains("deleted") || text.Contains("remove")) return "delete";
        if (text.Contains("renamed")) return "rename";
        if (text.Contains("saved") || text.Contains("update")) return "update";
        return "manual";
    }

    /// <inheritdoc />
    public List<SnapshotRoute> ParseSnapshotRoutes(JsonElement config)
    {
        var routes = new List<SnapshotRoute>();
        var reverseProxy = GetReverseProxySection(config);
        if (reverseProxy.ValueKind != JsonValueKind.Object) return routes;
        if (!(reverseProxy.TryGetProperty("routes", out var routesElement) || reverseProxy.TryGetProperty("Routes", out routesElement)))
            return routes;

        if (routesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var route in routesElement.EnumerateArray())
                routes.Add(ParseSnapshotRoute(route, null));
        }
        else if (routesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var route in routesElement.EnumerateObject())
                routes.Add(ParseSnapshotRoute(route.Value, route.Name));
        }

        return routes.Where(r => !string.IsNullOrWhiteSpace(r.RouteId)).ToList();
    }

    /// <inheritdoc />
    public List<SnapshotCluster> ParseSnapshotClusters(JsonElement config)
    {
        var clusters = new List<SnapshotCluster>();
        var reverseProxy = GetReverseProxySection(config);
        if (reverseProxy.ValueKind != JsonValueKind.Object) return clusters;
        if (!(reverseProxy.TryGetProperty("clusters", out var clustersElement) || reverseProxy.TryGetProperty("Clusters", out clustersElement)))
            return clusters;

        if (clustersElement.ValueKind != JsonValueKind.Object) return clusters;

        foreach (var kvp in clustersElement.EnumerateObject())
        {
            var clusterId = kvp.Name;
            var cluster = kvp.Value;

            var lbPolicy = (cluster.TryGetProperty("loadBalancingPolicy", out var lbp)
                ? lbp.GetString() : null)
                ?? (cluster.TryGetProperty("LoadBalancingPolicy", out var lbpp)
                ? lbpp.GetString() : null);

            var dests = new Dictionary<string, string>();
            if (cluster.TryGetProperty("destinations", out var d) || cluster.TryGetProperty("Destinations", out d))
            {
                if (d.ValueKind == JsonValueKind.Object)
                {
                    foreach (var dest in d.EnumerateObject())
                    {
                        var addr = dest.Value.ValueKind == JsonValueKind.String
                            ? dest.Value.GetString() ?? string.Empty
                            : dest.Value.TryGetProperty("address", out var a) ? a.GetString() ?? string.Empty :
                              dest.Value.TryGetProperty("Address", out var ap) ? ap.GetString() ?? string.Empty : string.Empty;
                        dests[dest.Name] = addr;
                    }
                }
            }

            clusters.Add(new SnapshotCluster
            {
                ClusterId = clusterId,
                LoadBalancingPolicy = lbPolicy,
                Destinations = dests
            });
        }

        return clusters;
    }

    /// <inheritdoc />
    public List<object> BuildRouteDiffs(
        List<SnapshotRoute> snapshotRoutes,
        IReadOnlyList<RouteConfig> currentRoutes)
    {
        var diffs = new List<object>();
        var currentSet = currentRoutes.ToDictionary(r => r.RouteId ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        foreach (var sr in snapshotRoutes)
        {
            if (currentSet.TryGetValue(sr.RouteId, out var live))
            {
                if (sr.ClusterId != (live.ClusterId ?? string.Empty) ||
                    sr.MatchPath != (live.Match?.Path ?? string.Empty) ||
                    sr.Order != (live.Order ?? 0))
                {
                    diffs.Add(GetDiffItem("route",
                        $"{sr.RouteId} ({sr.MatchPath} → {sr.ClusterId})",
                        "modified",
                        $"ClusterId: {sr.ClusterId}, Path: {sr.MatchPath}, Order: {sr.Order}",
                        $"ClusterId: {live.ClusterId}, Path: {live.Match?.Path}, Order: {live.Order}"));
                }
            }
            else
            {
                diffs.Add(GetDiffItem("route",
                    $"{sr.RouteId} ({sr.MatchPath} → {sr.ClusterId})",
                    "removed",
                    $"ClusterId: {sr.ClusterId}, Path: {sr.MatchPath}, Order: {sr.Order}"));
            }
        }

        var snapshotSet = snapshotRoutes.Select(s => s.RouteId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var live in currentRoutes)
        {
            if (!snapshotSet.Contains(live.RouteId ?? string.Empty))
            {
                diffs.Add(GetDiffItem("route",
                    $"{live.RouteId} ({live.Match?.Path} → {live.ClusterId})",
                    "added",
                    null,
                    $"ClusterId: {live.ClusterId}, Path: {live.Match?.Path}, Order: {live.Order}"));
            }
        }

        return diffs;
    }

    /// <inheritdoc />
    public List<object> BuildClusterDiffs(
        List<SnapshotCluster> snapshotClusters,
        IReadOnlyList<ClusterConfig> currentClusters)
    {
        var diffs = new List<object>();
        var currentSet = currentClusters.ToDictionary(c => c.ClusterId ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        foreach (var sc in snapshotClusters)
        {
            if (currentSet.TryGetValue(sc.ClusterId, out var live))
            {
                var snapshotDest = string.Join(", ", sc.Destinations.Select(d => $"{d.Key}={d.Value}"));
                var currentDest = string.Join(", ", live.Destinations?.Select(d => $"{d.Key}={d.Value.Address}") ?? Enumerable.Empty<string>());

                if (snapshotDest != currentDest || sc.LoadBalancingPolicy != live.LoadBalancingPolicy)
                {
                    diffs.Add(GetDiffItem("cluster",
                        sc.ClusterId,
                        "modified",
                        $"Destinations: [{snapshotDest}], Policy: {sc.LoadBalancingPolicy ?? "none"}",
                        $"Destinations: [{currentDest}], Policy: {live.LoadBalancingPolicy ?? "none"}"));
                }
            }
            else
            {
                var snapshotDest = string.Join(", ", sc.Destinations.Select(d => $"{d.Key}={d.Value}"));
                diffs.Add(GetDiffItem("cluster", sc.ClusterId, "removed",
                    $"Destinations: [{snapshotDest}], Policy: {sc.LoadBalancingPolicy ?? "none"}"));
            }
        }

        var snapshotSet = snapshotClusters.Select(s => s.ClusterId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var live in currentClusters)
        {
            if (!snapshotSet.Contains(live.ClusterId ?? string.Empty))
            {
                var dest = string.Join(", ", live.Destinations?.Select(d => $"{d.Key}={d.Value.Address}") ?? Enumerable.Empty<string>());
                diffs.Add(GetDiffItem("cluster", $"{live.ClusterId} ({dest})", "added",
                    null,
                    $"Destinations: [{dest}], Policy: {live.LoadBalancingPolicy ?? "none"}"));
            }
        }

        return diffs;
    }

    #region Private helpers

    /// <summary>Resolves the ReverseProxy section from a config JSON element.</summary>
    private static JsonElement GetReverseProxySection(JsonElement config)
    {
        if (config.ValueKind == JsonValueKind.Object &&
            (config.TryGetProperty("reverseProxy", out var rp) || config.TryGetProperty("ReverseProxy", out rp)))
        {
            return rp;
        }
        return default;
    }

    /// <summary>Parses a single snapshot route entry from a JSON element.</summary>
    private static SnapshotRoute ParseSnapshotRoute(JsonElement route, string? fallbackRouteId)
    {
        var routeId = route.TryGetProperty("routeId", out var rid) ? rid.GetString() ?? string.Empty :
            route.TryGetProperty("RouteId", out var ridp) ? ridp.GetString() ?? string.Empty : fallbackRouteId ?? string.Empty;

        var clusterId = route.TryGetProperty("clusterId", out var cid) ? cid.GetString() ?? string.Empty :
            route.TryGetProperty("ClusterId", out var cidp) ? cidp.GetString() ?? string.Empty : string.Empty;

        string matchPath = string.Empty;
        if (route.TryGetProperty("match", out var match) || route.TryGetProperty("Match", out match))
        {
            if (match.TryGetProperty("path", out var path) || match.TryGetProperty("Path", out path))
                matchPath = path.GetString() ?? string.Empty;
        }

        var order = route.TryGetProperty("order", out var ord) ? ord.GetInt32() :
            route.TryGetProperty("Order", out var ordp) ? ordp.GetInt32() : 0;

        return new SnapshotRoute
        {
            RouteId = routeId,
            ClusterId = clusterId,
            MatchPath = matchPath,
            Order = order
        };
    }

    /// <summary>Builds a diff item object for the diff result list.</summary>
    private static object GetDiffItem(string entityType, string path, string diffType, string? oldValue = null, string? newValue = null)
    {
        return new
        {
            type = diffType,
            path = $"[{entityType}] {path}",
            oldValue,
            newValue
        };
    }

    #endregion
}
