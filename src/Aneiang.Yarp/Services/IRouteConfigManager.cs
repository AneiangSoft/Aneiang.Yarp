using Aneiang.Yarp.Models;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

internal interface IRouteConfigManager
{
    Task<RouteOperationResult> TryAddRoute(RegisterRouteRequest request, string source, string? createdBy);

    Task<RouteOperationResult> TryAddRouteConfig(RouteConfig route, string source, string? createdBy);

    Task<RouteOperationResult> TryRemoveRoute(string routeName, string? clientIp, bool removeOrphanedCluster);

    Task<RouteOperationResult> TryRenameRoute(string oldRouteId, string newRouteId, RegisterRouteRequest request, string source, string? createdBy);

    Task<bool> UpdateRouteMetadataAsync(string routeId, Dictionary<string, string> metadata);

    IReadOnlyList<RouteConfig> GetRoutes();

    GatewayDynamicConfig GetDynamicConfig();
}
