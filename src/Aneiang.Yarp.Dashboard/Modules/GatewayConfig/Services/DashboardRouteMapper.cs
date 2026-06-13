using Aneiang.Yarp.Dashboard.Modules.Dashboard.Models;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;

/// <summary>
/// Maps route configuration to dashboard response DTO.
/// Optimized to avoid reflection by using strong-typed structs.
/// </summary>
internal static class DashboardRouteMapper
{
    /// <summary>
    /// Maps a route configuration to dashboard response.
    /// Uses strong-typed DestinationInfoEntry to eliminate reflection overhead.
    /// </summary>
    public static DashboardRouteResponse MapToResponse(
        RouteConfig route,
        IReadOnlyDictionary<string, List<DestinationInfoEntry>> clusterDestinations,
        IReadOnlyDictionary<string, List<Dictionary<string, string>>>? configTransforms,
        IReadOnlyDictionary<string, string>? routeSources)
    {
        var match = route.Match;

        // Map destinations using strong-typed struct (no reflection)
        List<RouteDestinationInfo>? destinations = null;
        if (route.ClusterId != null && clusterDestinations.TryGetValue(route.ClusterId, out var dests))
        {
            destinations = new List<RouteDestinationInfo>(dests.Count);
            foreach (var d in dests)
            {
                destinations.Add(new RouteDestinationInfo
                {
                    Name = d.Name,
                    Address = d.Address
                });
            }
        }

        // Reuse transforms or create new
        List<Dictionary<string, string>>? transforms = null;
        if (route.Transforms?.Count > 0)
        {
            transforms = new List<Dictionary<string, string>>(route.Transforms.Count);
            foreach (var t in route.Transforms)
            {
                transforms.Add(new Dictionary<string, string>(t));
            }
        }
        else if (configTransforms != null && configTransforms.TryGetValue(route.RouteId, out var ct) && ct != null)
        {
            transforms = ct;
        }

        var (isEditable, source) = GetRouteEditability(route.RouteId, routeSources);

        // Pre-calculate metadata dictionary if needed
        Dictionary<string, string>? metadataDict = null;
        if (route.Metadata?.Count > 0)
        {
            metadataDict = new Dictionary<string, string>(route.Metadata.Count);
            foreach (var kv in route.Metadata)
            {
                metadataDict[kv.Key] = kv.Value;
            }
        }

        return new DashboardRouteResponse
        {
            RouteId = route.RouteId,
            ClusterId = route.ClusterId,
            Match = new RouteMatchInfo
            {
                Path = match?.Path,
                Methods = match?.Methods,
                Hosts = match?.Hosts,
                Headers = MapHeaders(match?.Headers),
                QueryParameters = MapQueryParameters(match?.QueryParameters)
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
            Metadata = metadataDict,
            Source = source,
            IsEditable = isEditable
        };
    }

    /// <summary>
    /// Maps header matches with pre-allocated list capacity for efficiency.
    /// </summary>
    private static List<RouteHeaderInfo>? MapHeaders(IReadOnlyList<RouteHeader>? headers)
    {
        if (headers == null || headers.Count == 0)
            return null;

        var result = new List<RouteHeaderInfo>(headers.Count);
        foreach (var h in headers)
        {
            result.Add(new RouteHeaderInfo
            {
                Name = h.Name,
                Values = h.Values,
                Mode = h.Mode.ToString()
            });
        }
        return result;
    }

    /// <summary>
    /// Maps query parameter matches with pre-allocated list capacity for efficiency.
    /// </summary>
    private static List<RouteQueryParameterInfo>? MapQueryParameters(IReadOnlyList<RouteQueryParameter>? parameters)
    {
        if (parameters == null || parameters.Count == 0)
            return null;

        var result = new List<RouteQueryParameterInfo>(parameters.Count);
        foreach (var q in parameters)
        {
            result.Add(new RouteQueryParameterInfo
            {
                Name = q.Name,
                Values = q.Values,
                Mode = q.Mode.ToString()
            });
        }
        return result;
    }

    /// <summary>
    /// Determines editability and source for a route.
    /// All routes are editable. Source is preserved for display purposes only.
    /// </summary>
    private static (bool isEditable, string source) GetRouteEditability(string routeId, IReadOnlyDictionary<string, string>? routeSources)
    {
        if (routeSources != null && routeSources.TryGetValue(routeId, out var source))
        {
            return (true, source);
        }

        return (true, "config"); // Not in route sources means it's from static config
    }
}
