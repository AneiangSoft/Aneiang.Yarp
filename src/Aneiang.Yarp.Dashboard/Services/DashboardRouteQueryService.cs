using Aneiang.Yarp.Dashboard.Models.Dtos;
using Aneiang.Yarp.Services;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Implementation of dashboard route query service.
/// </summary>
internal sealed class DashboardRouteQueryService : IDashboardRouteQueryService
{
    private readonly DynamicYarpConfigService _dynamicConfig;

    /// <summary>
    /// Initializes a new instance of DashboardRouteQueryService.
    /// </summary>
    /// <param name="dynamicConfig">Dynamic YARP config service.</param>
    public DashboardRouteQueryService(
        DynamicYarpConfigService dynamicConfig)
    {
        _dynamicConfig = dynamicConfig;
    }

    /// <inheritdoc />
    public IReadOnlyList<DashboardRouteResponse> GetRoutes()
    {
        var routes = _dynamicConfig.GetRoutes();
        
        var clusterDest = _dynamicConfig.GetClusters()
            .ToDictionary(
                c => c.ClusterId,
                c => c.Destinations?.Select(d => (object)new
                {
                    name = d.Key,
                    address = d.Value.Address
                }).ToList() ?? new List<object>(),
                StringComparer.OrdinalIgnoreCase);

        Dictionary<string, List<Dictionary<string, string>>>? configTransforms = null;
        Dictionary<string, string>? routeSources = null;

        try
        {
            if (routes?.Count > 0)
            {
                configTransforms = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
                routeSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                var dynConfig = _dynamicConfig.GetDynamicConfig();

                foreach (var r in routes)
                {
                    if (r.Transforms?.Count > 0)
                    {
                        configTransforms[r.RouteId] = r.Transforms
                            .Select(t => new Dictionary<string, string>(t))
                            .ToList();
                    }

                    var dynRoute = dynConfig?.Routes.FirstOrDefault(dr =>
                        string.Equals(dr.RouteId, r.RouteId, StringComparison.OrdinalIgnoreCase));
                    if (dynRoute != null)
                    {
                        routeSources[r.RouteId] = dynRoute.Source;
                    }
                }
            }
        }
        catch (Exception)
        {
            // Log transform extraction failure but continue rendering routes
        }

        return routes?
            .Select(route => DashboardRouteMapper.MapToResponse(
                route,
                clusterDest,
                configTransforms,
                routeSources))
            .OrderBy(r => r.Order)
            .ToList() ?? new List<DashboardRouteResponse>();
    }
}
