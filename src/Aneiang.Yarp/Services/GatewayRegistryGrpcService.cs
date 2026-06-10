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
        if (request.Destinations.Count == 0 || request.Destinations.All(d => string.IsNullOrWhiteSpace(d.Address)))
        {
            return new RegisterServiceResponse
            {
                Success = false,
                Message = "At least one valid destination address is required"
            };
        }

        GrpcGatewayRegistryMapper.LogUnsupportedPathsIfNeeded(request, _logger);

        var registerRequest = GrpcGatewayRegistryMapper.ToRegisterRouteRequest(request);
        var result = await _dynamicConfigService.TryAddRoute(registerRequest, "grpc", "grpc-client");

        if (result.Success)
        {
            var cluster = new Dictionary<string, string>
            {
                ["d1"] = registerRequest.DestinationAddress
            };

            await _dynamicConfigService.TryAddCluster(
                GrpcGatewayRegistryMapper.BuildClusterName(request),
                cluster,
                GrpcGatewayRegistryMapper.MapLoadBalancingPolicy(request.LoadBalancing),
                source: "grpc",
                createdBy: "grpc-client");
        }

        return new RegisterServiceResponse
        {
            Success = result.Success,
            Message = result.Message,
            ClusterId = GrpcGatewayRegistryMapper.BuildClusterName(request),
            RegisteredAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
    }

    public override async Task<UnregisterServiceResponse> UnregisterService(UnregisterServiceRequest request, ServerCallContext context)
    {
        var routeName = string.IsNullOrWhiteSpace(request.ServiceId) ? "grpc-service" : request.ServiceId;
        var result = await _dynamicConfigService.TryRemoveRoute(routeName);

        return new UnregisterServiceResponse
        {
            Success = result.Success,
            Message = result.Message
        };
    }

    public override Task<HeartbeatResponse> Heartbeat(HeartbeatRequest request, ServerCallContext context)
    {
        var routeName = string.IsNullOrWhiteSpace(request.ServiceId) ? "grpc-service" : request.ServiceId;
        var updated = _dynamicConfigService.UpdateHeartbeat(routeName);

        return Task.FromResult(new HeartbeatResponse
        {
            Success = updated,
            Message = updated ? "heartbeat" : $"Route '{routeName}' not found",
            NextHeartbeat = DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeSeconds()
        });
    }

    public override Task<GetServicesResponse> GetServices(GetServicesRequest request, ServerCallContext context)
    {
        var dynamicConfig = _dynamicConfigService.GetDynamicConfig();
        var services = new List<ServiceInfo>();

        if (dynamicConfig != null)
        {
            foreach (var route in dynamicConfig.Routes.Where(r => string.Equals(r.Source, "grpc", StringComparison.OrdinalIgnoreCase)))
            {
                var cluster = dynamicConfig.Clusters.FirstOrDefault(c => string.Equals(c.ClusterId, route.ClusterId, StringComparison.OrdinalIgnoreCase));
                if (request.ActiveOnly && cluster == null)
                    continue;

                var serviceInfo = new ServiceInfo
                {
                    ServiceId = route.RouteId,
                    ServiceName = route.ClusterId,
                    ClusterId = route.ClusterId,
                    IsHealthy = true,
                    RegisteredAt = new DateTimeOffset(route.CreatedAt).ToUnixTimeSeconds(),
                    LastHeartbeat = cluster?.LastHeartbeat != null
                        ? new DateTimeOffset(cluster.LastHeartbeat.Value).ToUnixTimeSeconds()
                        : 0
                };

                if (!string.IsNullOrWhiteSpace(route.MatchPath))
                    serviceInfo.Paths.Add(route.MatchPath);

                if (cluster?.Destinations != null)
                {
                    foreach (var destination in cluster.Destinations)
                    {
                        serviceInfo.Destinations.Add(new DestinationInfo
                        {
                            DestinationId = destination.Key,
                            Address = destination.Value,
                            IsHealthy = true,
                            IsEnabled = true,
                            HealthRatio = 1
                        });
                    }
                }

                services.Add(serviceInfo);
            }
        }

        var response = new GetServicesResponse();
        response.Services.AddRange(services);
        return Task.FromResult(response);
    }

    public override async Task<UpdateDestinationsResponse> UpdateDestinations(UpdateDestinationsRequest request, ServerCallContext context)
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

        var result = await _dynamicConfigService.TryAddCluster(
            clusterId,
            destinations,
            source: "grpc",
            createdBy: "grpc-client");

        return new UpdateDestinationsResponse
        {
            Success = result.Success,
            Message = result.Message
        };
    }
}
