using Aneiang.Yarp.GatewayRegistry;
using Aneiang.Yarp.Models;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.LoadBalancing;

namespace Aneiang.Yarp.Services;

internal static class GrpcGatewayRegistryMapper
{
    /// <summary>
    /// Phase 2: Convert a gRPC RegisterServiceRequest to one or more route requests.
    /// Multiple paths → multiple route configs sharing the same cluster.
    /// All valid destinations are kept in the cluster for load balancing.
    /// </summary>
    public static List<RegisterRouteRequest> ToRegisterRouteRequests(RegisterServiceRequest request)
    {
        var routeName = BuildRouteName(request);
        var clusterName = BuildClusterName(request);
        var destinations = request.Destinations
            .Where(d => !string.IsNullOrWhiteSpace(d.Address))
            .ToList();

        // Normalize paths
        var paths = request.Paths.Count > 0
            ? request.Paths.Select(NormalizeMatchPath).ToList()
            : new List<string> { "/{**catch-all}" };

        // Primary destination address (first valid)
        var primaryAddress = destinations.FirstOrDefault()?.Address ?? string.Empty;

        // Build one route request per path, suffixed with path index for unique route IDs
        var routeRequests = new List<RegisterRouteRequest>();
        for (int i = 0; i < paths.Count; i++)
        {
            var pathRouteName = paths.Count > 1
                ? $"{routeName}-path{i}"
                : routeName;

            routeRequests.Add(new RegisterRouteRequest
            {
                RouteName = pathRouteName,
                ClusterName = clusterName,
                MatchPath = paths[i],
                DestinationAddress = primaryAddress,
                Order = int.MaxValue,
                UseIpIsolation = false
            });
        }

        return routeRequests;
    }

    /// <summary>
    /// Build a destinations dictionary from the gRPC request (all valid destinations).
    /// Phase 2: keeps all destinations for load balancing.
    /// </summary>
    public static Dictionary<string, string> BuildDestinations(RegisterServiceRequest request)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dest in request.Destinations)
        {
            if (string.IsNullOrWhiteSpace(dest.Address))
                continue;

            var destId = string.IsNullOrWhiteSpace(dest.DestinationId)
                ? $"dest-{Guid.NewGuid():N}"
                : dest.DestinationId;

            dict[destId] = dest.Address;
        }
        return dict;
    }

    public static string BuildRouteName(RegisterServiceRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ServiceId))
            return request.ServiceId;

        if (!string.IsNullOrWhiteSpace(request.ServiceName))
            return request.ServiceName;

        return "grpc-service";
    }

    public static string BuildClusterName(RegisterServiceRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ServiceName))
            return request.ServiceName;

        return BuildRouteName(request);
    }

    public static string MapLoadBalancingPolicy(LoadBalancingPolicy policy)
    {
        return policy switch
        {
            LoadBalancingPolicy.RoundRobin => LoadBalancingPolicies.RoundRobin,
            LoadBalancingPolicy.Random => LoadBalancingPolicies.Random,
            LoadBalancingPolicy.LeastRequests => LoadBalancingPolicies.LeastRequests,
            LoadBalancingPolicy.PowerOfTwoChoices => LoadBalancingPolicies.PowerOfTwoChoices,
            _ => LoadBalancingPolicies.RoundRobin
        };
    }

    public static string NormalizeMatchPath(string path)
    {
        var trimmed = path.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return "/{**catch-all}";

        if (!trimmed.StartsWith('/'))
            trimmed = "/" + trimmed;

        if (!trimmed.Contains("{**catch-all}", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.Contains("{**catchAll}", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.TrimEnd('/') + "/{**catch-all}";
        }

        return trimmed;
    }

    public static void LogUnsupportedPathsIfNeeded(RegisterServiceRequest request, ILogger logger)
    {
        // Phase 2: no longer a limitation — multi-path is supported
        if (request.Paths.Count > 1)
        {
            logger.LogDebug(
                "gRPC registration for service {ServiceId}: {PathCount} paths → {RouteCount} routes",
                BuildRouteName(request),
                request.Paths.Count,
                request.Paths.Count);
        }

        if (request.Destinations.Count > 1)
        {
            logger.LogDebug(
                "gRPC registration for service {ServiceId}: {DestinationCount} destinations added to cluster",
                BuildRouteName(request),
                request.Destinations.Count);
        }
    }
}
