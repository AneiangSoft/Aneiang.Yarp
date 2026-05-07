using Aneiang.Yarp.Dashboard.Models.Dtos;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Maps route configuration to dashboard response DTO.
/// </summary>
internal static class DashboardRouteMapper
{
    /// <summary>
    /// Maps a route configuration to dashboard response.
    /// </summary>
    public static DashboardRouteResponse MapToResponse(
        RouteConfig route,
        IReadOnlyDictionary<string, List<object>> clusterDestinations,
        IReadOnlyDictionary<string, List<Dictionary<string, string>>>? configTransforms,
        IReadOnlyDictionary<string, string>? routeSources)
    {
        var match = route.Match;

        List<RouteDestinationInfo>? destinations = null;
        if (route.ClusterId != null && clusterDestinations.TryGetValue(route.ClusterId, out var dests))
        {
            destinations = dests.Select(d => new RouteDestinationInfo
            {
                Name = d.GetType().GetProperty("name")?.GetValue(d)?.ToString() ?? string.Empty,
                Address = d.GetType().GetProperty("address")?.GetValue(d)?.ToString()
            }).ToList();
        }

        List<Dictionary<string, string>>? transforms = null;
        if (route.Transforms?.Count > 0)
        {
            transforms = route.Transforms.Select(t => new Dictionary<string, string>(t)).ToList();
        }
        else if (configTransforms != null && configTransforms.TryGetValue(route.RouteId, out var ct) && ct != null)
        {
            transforms = ct;
        }

        var isEditable = IsRouteEditable(route.RouteId, routeSources);

        return new DashboardRouteResponse
        {
            RouteId = route.RouteId,
            ClusterId = route.ClusterId,
            Match = new RouteMatchInfo
            {
                Path = match?.Path,
                Methods = match?.Methods,
                Hosts = match?.Hosts,
                Headers = match?.Headers?.Count > 0
                    ? match.Headers.Select(h => new RouteHeaderInfo
                    {
                        Name = h.Name,
                        Values = h.Values,
                        Mode = h.Mode.ToString()
                    }).ToList()
                    : null,
                QueryParameters = match?.QueryParameters?.Count > 0
                    ? match.QueryParameters.Select(q => new RouteQueryParameterInfo
                    {
                        Name = q.Name,
                        Values = q.Values,
                        Mode = q.Mode.ToString()
                    }).ToList()
                    : null
            },
            Order = route.Order,
            AuthorizationPolicy = route.AuthorizationPolicy,
            CorsPolicy = route.CorsPolicy,
            OutputCachePolicy = route.OutputCachePolicy,
            MaxRequestBodySize = route.MaxRequestBodySize,
            Destinations = destinations,
            Transforms = transforms,
            RateLimiterPolicy = route.RateLimiterPolicy,
            TimeoutPolicy = route.TimeoutPolicy,
            Timeout = route.Timeout?.ToString(),
            Metadata = route.Metadata?.Count > 0 ? route.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value) : null,
            IsEditable = isEditable
        };
    }

    /// <summary>
    /// Determines if a route is editable based on its source.
    /// </summary>
    private static bool IsRouteEditable(string routeId, IReadOnlyDictionary<string, string>? routeSources)
    {
        if (routeSources != null && routeSources.TryGetValue(routeId, out var source))
        {
            return source != "config";
        }

        return true;
    }
}
