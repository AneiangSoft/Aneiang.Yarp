using Aneiang.Yarp.GatewayRegistry;
using Aneiang.Yarp.Models;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.LoadBalancing;

namespace Aneiang.Yarp.Services;

internal static class GrpcGatewayRegistryMapper
{
    public static RegisterRouteRequest ToRegisterRouteRequest(RegisterServiceRequest request)
    {
        var primaryPath = request.Paths.FirstOrDefault();
        var normalizedPath = string.IsNullOrWhiteSpace(primaryPath) ? "/{**catch-all}" : NormalizeMatchPath(primaryPath);
        var destination = request.Destinations.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.Address));

        return new RegisterRouteRequest
        {
            RouteName = BuildRouteName(request),
            ClusterName = BuildClusterName(request),
            MatchPath = normalizedPath,
            DestinationAddress = destination?.Address ?? string.Empty,
            Order = 50,
            UseIpIsolation = false
        };
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
            LoadBalancingPolicy.RoundRobin => "RoundRobin",
            LoadBalancingPolicy.Random => "Random",
            LoadBalancingPolicy.LeastRequests => "LeastRequests",
            LoadBalancingPolicy.PowerOfTwoChoices => "PowerOfTwoChoices",
            _ => "RoundRobin"
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
        if (request.Paths.Count > 1)
        {
            logger.LogInformation(
                "gRPC registration for service {ServiceId} provided {PathCount} paths. Phase 1 keeps only the first path: {PrimaryPath}",
                BuildRouteName(request),
                request.Paths.Count,
                request.Paths.FirstOrDefault() ?? "n/a");
        }

        if (request.Destinations.Count > 1)
        {
            logger.LogInformation(
                "gRPC registration for service {ServiceId} provided {DestinationCount} destinations. Phase 1 keeps only the first valid destination.",
                BuildRouteName(request),
                request.Destinations.Count);
        }
    }
}
