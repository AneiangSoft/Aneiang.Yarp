using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;

public interface IGatewayIdentityService
{
    Task<RouteOperationResult> RenameClusterAsync(
        string oldClusterId,
        string newClusterId,
        Dictionary<string, string> destinations,
        string? loadBalancingPolicy = null,
        HealthCheckConfig? healthCheck = null,
        string? clientIp = null,
        string? operatorName = "dashboard-user",
        CancellationToken ct = default);

    Task<RouteOperationResult> RenameRouteAsync(
        string oldRouteId,
        string newRouteId,
        RegisterRouteRequest request,
        string? clientIp = null,
        string? operatorName = "dashboard-user",
        CancellationToken ct = default);

    Task AfterClusterRenamedAsync(string oldClusterId, string newClusterId, CancellationToken ct = default);
    Task AfterRouteRenamedAsync(string oldRouteId, string newRouteId, CancellationToken ct = default);
}
