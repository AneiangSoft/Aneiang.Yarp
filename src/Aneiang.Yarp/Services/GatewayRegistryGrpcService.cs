using GatewayGrpc = Aneiang.Yarp.GatewayRegistry.GatewayRegistry;
using Aneiang.Yarp.GatewayRegistry;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Services;

public class GatewayRegistryGrpcService : GatewayGrpc.GatewayRegistryBase
{
    private readonly DynamicYarpConfigService _dynamicConfigService;
    private readonly ILogger<GatewayRegistryGrpcService> _logger;

    public GatewayRegistryGrpcService(
        DynamicYarpConfigService dynamicConfigService,
        ILogger<GatewayRegistryGrpcService> logger)
    {
        _dynamicConfigService = dynamicConfigService;
        _logger = logger;
    }

    public override async Task<RegisterServiceResponse> RegisterService(RegisterServiceRequest request, ServerCallContext context)
    {
        try
        {
            return await RegisterServiceInternalAsync(request, context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "gRPC RegisterService unhandled exception for ServiceId={ServiceId}", request.ServiceId);
            return new RegisterServiceResponse
            {
                Success = false,
                Message = $"Internal server error: {ex.Message}"
            };
        }
    }

    private async Task<RegisterServiceResponse> RegisterServiceInternalAsync(RegisterServiceRequest request, ServerCallContext context)
    {
        if (request.Destinations.Count == 0 || request.Destinations.All(d => string.IsNullOrWhiteSpace(d.Address)))
        {
            return new RegisterServiceResponse
            {
                Success = false,
                Message = "At least one valid destination address is required"
            };
        }

        GrpcGatewayRegistryMapper.LogUnsupportedPathsIfNeeded(request, _logger);

        // Phase 2: multi-path → multiple routes sharing the same cluster
        var routeRequests = GrpcGatewayRegistryMapper.ToRegisterRouteRequests(request);
        var clusterName = GrpcGatewayRegistryMapper.BuildClusterName(request);
        var destinations = GrpcGatewayRegistryMapper.BuildDestinations(request);
        var loadBalancingPolicy = GrpcGatewayRegistryMapper.MapLoadBalancingPolicy(request.LoadBalancing);

        // Register all routes
        var routeIds = new List<string>();
        var allSucceeded = true;
        var messages = new List<string>();

        foreach (var routeReq in routeRequests)
        {
            var result = await _dynamicConfigService.TryAddRoute(routeReq, "grpc", "grpc-client");
            if (result.Success)
            {
                routeIds.Add(routeReq.RouteName);
            }
            else
            {
                allSucceeded = false;
                messages.Add($"[{routeReq.RouteName}] {result.Message}");
            }
        }

        // Register the cluster with all destinations
        if (routeIds.Count > 0)
        {
            var metadata = request.Metadata.Count > 0
                ? request.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value)
                : null;

            var clusterResult = await _dynamicConfigService.TryAddCluster(
                clusterName,
                destinations,
                loadBalancingPolicy,
                source: "grpc",
                createdBy: "grpc-client",
                metadata: metadata);

            if (!clusterResult.Success)
            {
                _logger.LogWarning("gRPC cluster registration warning: {Message}", clusterResult.Message);
                if (allSucceeded)
                    messages.Add($"Cluster: {clusterResult.Message}");
            }
        }

        var response = new RegisterServiceResponse
        {
            Success = allSucceeded && routeIds.Count > 0,
            Message = string.Join("; ", messages),
            ClusterId = clusterName,
            RegisteredAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        response.RouteIds.AddRange(routeIds);

        if (allSucceeded)
            response.Message = $"Created {routeIds.Count} route(s) in cluster '{clusterName}'";

        return response;
    }

    public override async Task<UnregisterServiceResponse> UnregisterService(UnregisterServiceRequest request, ServerCallContext context)
    {
        try
        {
            var serviceId = string.IsNullOrWhiteSpace(request.ServiceId) ? "grpc-service" : request.ServiceId;

            // Multi-path registration creates routes named "{serviceId}" (single path)
            // or "{serviceId}-path{N}" (one per path). Remove them all; TryRemoveRoute
            // already cleans up the cluster when the last referencing route is removed.
            var routeNames = FindServiceRouteNames(serviceId);
            if (routeNames.Count == 0)
            {
                return new UnregisterServiceResponse
                {
                    Success = false,
                    Message = $"Route '{serviceId}' not found"
                };
            }

            var removed = new List<string>();
            var failed = new List<string>();
            foreach (var name in routeNames)
            {
                var result = await _dynamicConfigService.TryRemoveRoute(name);
                if (result.Success) removed.Add(name);
                else failed.Add($"[{name}] {result.Message}");
            }

            var success = failed.Count == 0;
            return new UnregisterServiceResponse
            {
                Success = success,
                Message = success
                    ? $"Removed {removed.Count} route(s): {string.Join(", ", removed)}"
                    : string.Join("; ", failed)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "gRPC UnregisterService exception for ServiceId={ServiceId}", request.ServiceId);
            return new UnregisterServiceResponse { Success = false, Message = $"Error: {ex.Message}" };
        }
    }

    public override Task<HeartbeatResponse> Heartbeat(HeartbeatRequest request, ServerCallContext context)
    {
        try
        {
            var serviceId = string.IsNullOrWhiteSpace(request.ServiceId) ? "grpc-service" : request.ServiceId;

            // Resolve the route name: "{serviceId}" when present, otherwise any
            // "{serviceId}-path{N}" route created by multi-path registration.
            var routeName = ResolveHeartbeatRouteName(serviceId);
            var updated = routeName != null && _dynamicConfigService.UpdateHeartbeat(routeName);

            return Task.FromResult(new HeartbeatResponse
            {
                Success = updated,
                Message = updated ? "heartbeat" : $"Route '{serviceId}' not found",
                NextHeartbeat = DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeSeconds()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "gRPC Heartbeat exception for ServiceId={ServiceId}", request.ServiceId);
            return Task.FromResult(new HeartbeatResponse { Success = false, Message = $"Error: {ex.Message}" });
        }
    }

    /// <summary>
    /// Find all route names belonging to a service: the exact "{serviceId}" route and any
    /// "{serviceId}-path{N}" routes created by multi-path registration.
    /// </summary>
    private List<string> FindServiceRouteNames(string serviceId)
    {
        var names = new List<string>();
        var config = _dynamicConfigService.GetDynamicConfig();
        if (config == null) return names;

        foreach (var route in config.Routes)
        {
            var routeId = route.Config.RouteId;
            if (string.IsNullOrWhiteSpace(routeId)) continue;

            if (routeId.Equals(serviceId, StringComparison.OrdinalIgnoreCase) ||
                IsMultiPathRouteName(routeId, serviceId))
            {
                names.Add(routeId);
            }
        }

        return names;
    }

    /// <summary>
    /// Resolve a route name usable for heartbeat: the exact "{serviceId}" route, or the first
    /// "{serviceId}-path{N}" route when multi-path registration was used. Returns null when none exist.
    /// </summary>
    private string? ResolveHeartbeatRouteName(string serviceId)
    {
        var config = _dynamicConfigService.GetDynamicConfig();
        if (config == null) return null;

        foreach (var route in config.Routes)
        {
            var routeId = route.Config.RouteId;
            if (string.IsNullOrWhiteSpace(routeId)) continue;
            if (routeId.Equals(serviceId, StringComparison.OrdinalIgnoreCase))
                return routeId;
        }

        foreach (var route in config.Routes)
        {
            var routeId = route.Config.RouteId;
            if (string.IsNullOrWhiteSpace(routeId)) continue;
            if (IsMultiPathRouteName(routeId, serviceId))
                return routeId;
        }

        return null;
    }

    /// <summary>Checks whether "{routeId}" is a multi-path child route of "{serviceId}" (i.e. "{serviceId}-path{N}").</summary>
    private static bool IsMultiPathRouteName(string routeId, string serviceId)
    {
        return routeId.Length > serviceId.Length + 5 &&
            routeId.StartsWith(serviceId + "-path", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(routeId[(serviceId.Length + 5)..], out _);
    }

    public override Task<GetServicesResponse> GetServices(GetServicesRequest request, ServerCallContext context)
    {
        try
        {
            var dynamicConfig = _dynamicConfigService.GetDynamicConfig();
            var services = new List<ServiceInfo>();

            if (dynamicConfig != null)
            {
                foreach (var route in dynamicConfig.Routes.Where(r => string.Equals(r.Source, "grpc", StringComparison.OrdinalIgnoreCase)))
                {
                    var cluster = dynamicConfig.Clusters.FirstOrDefault(c => string.Equals(c.Config.ClusterId, route.Config.ClusterId, StringComparison.OrdinalIgnoreCase));
                    if (request.ActiveOnly && cluster == null)
                        continue;

                    var serviceInfo = new ServiceInfo
                    {
                        ServiceId = route.Config.RouteId,
                        ServiceName = route.Config.ClusterId,
                        ClusterId = route.Config.ClusterId,
                        IsHealthy = true,
                        RegisteredAt = new DateTimeOffset(route.CreatedAt).ToUnixTimeSeconds(),
                        LastHeartbeat = cluster?.LastHeartbeat != null
                            ? new DateTimeOffset(cluster.LastHeartbeat.Value).ToUnixTimeSeconds()
                            : 0
                    };

                    var matchPath = route.Config.Match?.Path;
                    if (!string.IsNullOrWhiteSpace(matchPath))
                        serviceInfo.Paths.Add(matchPath);

                    if (cluster?.Config.Destinations != null)
                    {
                        foreach (var destination in cluster.Config.Destinations)
                        {
                            serviceInfo.Destinations.Add(new DestinationInfo
                            {
                                DestinationId = destination.Key,
                                Address = destination.Value.Address ?? string.Empty,
                                IsHealthy = true,
                                IsEnabled = true,
                                HealthRatio = 1
                            });
                        }
                    }

                    if (cluster?.Config.Metadata != null)
                    {
                        foreach (var kv in cluster.Config.Metadata)
                            serviceInfo.Metadata[kv.Key] = kv.Value;
                    }

                    services.Add(serviceInfo);
                }
            }

            var response = new GetServicesResponse();
            response.Services.AddRange(services);
            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "gRPC GetServices exception");
            return Task.FromResult(new GetServicesResponse());
        }
    }

    public override async Task<UpdateDestinationsResponse> UpdateDestinations(UpdateDestinationsRequest request, ServerCallContext context)
    {
        try
        {
            if (request.Destinations.Count == 0)
            {
                return new UpdateDestinationsResponse
                {
                    Success = false,
                    Message = "At least one destination is required"
                };
            }

            var clusterId = request.ServiceId;
            var destinations = request.Destinations
                .Where(d => !string.IsNullOrWhiteSpace(d.Address))
                .ToDictionary(
                    d => string.IsNullOrWhiteSpace(d.DestinationId) ? $"dest-{Guid.NewGuid():N}" : d.DestinationId,
                    d => d.Address);

            // Preserve the cluster's existing load balancing policy — TryAddCluster
            // overwrites the whole cluster config, so passing null would silently reset it.
            var existingCluster = _dynamicConfigService.GetDynamicConfig()
                ?.Clusters.FirstOrDefault(c => string.Equals(c.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
            var loadBalancingPolicy = existingCluster?.Config.LoadBalancingPolicy;

            var result = await _dynamicConfigService.TryAddCluster(
                clusterId,
                destinations,
                loadBalancingPolicy,
                healthCheck: null,
                source: "grpc",
                createdBy: "grpc-client");

            return new UpdateDestinationsResponse
            {
                Success = result.Success,
                Message = result.Message
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "gRPC UpdateDestinations exception for ServiceId={ServiceId}", request.ServiceId);
            return new UpdateDestinationsResponse { Success = false, Message = $"Error: {ex.Message}" };
        }
    }
}
