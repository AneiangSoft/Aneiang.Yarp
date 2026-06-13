using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Aneiang.Yarp.Extensions;

public static class AneiangYarpEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapAneiangYarpGrpc(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGrpcService<GatewayRegistryGrpcService>();
        return endpoints;
    }
}
