using Aneiang.Yarp.Models;
using Aneiang.Yarp.Storage;

namespace Aneiang.Yarp.Dashboard.Modules.AI.Tools;

public partial class GatewayToolExecutor
{
    // ===================== ROUTE TOOLS =====================

    private object ExecuteGetRoutes()
    {
        var routes = _routeQuery.GetRoutes();
        return new
        {
            total = routes.Count,
            routes = routes.Select(r => new
            {
                route_id = r.RouteId,
                cluster_id = r.ClusterId,
                path = r.Match?.Path ?? "*",
                methods = r.Match?.Methods,
                source = r.Source,
                order = r.Order
            })
        };
    }

    private async Task<object> ExecuteCreateRouteAsync(ToolArgs args, CancellationToken ct)
    {
        var routeName = args.Get("route_name");
        var path = args.Get("path");
        var clusterId = args.Get("cluster_id");
        var destAddress = args.Get("destination_address");

        var request = new RegisterRouteRequest
        {
            RouteName = routeName,
            MatchPath = path,
            ClusterName = clusterId,
            DestinationAddress = destAddress
        };

        var result = await _dynamicConfig.TryAddRoute(request, "ai-assistant", "ai");
        return new
        {
            success = result.Success,
            message = result.Success
                ? $"Route '{routeName}' created/updated successfully."
                : $"Failed to create route: {result.Message}",
            route_id = routeName
        };
    }

    private async Task<object> ExecuteDeleteRouteAsync(ToolArgs args, CancellationToken ct)
    {
        var routeId = args.Get("route_id");
        var result = await _dynamicConfig.TryRemoveRoute(routeId, "ai-assistant");
        return new
        {
            success = result.Success,
            message = result.Success
                ? $"Route '{routeId}' deleted successfully."
                : $"Failed to delete route: {result.Message}"
        };
    }

    private async Task<object> ExecuteRenameRouteAsync(ToolArgs args)
    {
        var oldRouteId = args.Get("old_route_id");
        var newRouteId = args.Get("new_route_id");

        var routes = _routeQuery.GetRoutes();
        var existing = routes.FirstOrDefault(r =>
            string.Equals(r.RouteId, oldRouteId, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
            return new { success = false, message = $"Route '{oldRouteId}' not found." };

        var request = new RegisterRouteRequest
        {
            RouteName = newRouteId,
            MatchPath = existing.Match?.Path ?? "/",
            ClusterName = existing.ClusterId,
            DestinationAddress = existing.Destinations?.FirstOrDefault().Address ?? ""
        };

        var result = await _dynamicConfig.TryRenameRoute(oldRouteId, newRouteId, request, "ai-assistant", "ai");
        return new
        {
            success = result.Success,
            message = result.Success
                ? $"Route renamed: '{oldRouteId}' → '{newRouteId}'."
                : $"Failed to rename route: {result.Message}"
        };
    }
}
