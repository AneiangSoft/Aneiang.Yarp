using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Aneiang.Yarp.Extensions;
/// <summary>
/// The aneiang yarp endpoint route builder extensions.
/// </summary>

public static class AneiangYarpEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the aneiang yarp grpc.
    /// </summary>
    /// <param name="endpoints">The endpoints.</param>
    /// <returns>An IEndpointRouteBuilder.</returns>
    public static IEndpointRouteBuilder MapAneiangYarpGrpc(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGrpcService<GatewayRegistryGrpcService>();
        return endpoints;
    }
}
