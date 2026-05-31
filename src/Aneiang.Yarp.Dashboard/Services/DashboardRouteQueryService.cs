using System.Collections.Concurrent;
using Aneiang.Yarp.Dashboard.Models.Dtos;
using Aneiang.Yarp.Services;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Lightweight DTO for destination info - eliminates reflection in mapper.
/// </summary>
internal readonly struct DestinationInfoEntry
{
    public string Name { get; init; }
    public string? Address { get; init; }

    public DestinationInfoEntry(string name, string? address)
    {
        Name = name;
        Address = address;
    }
}

/// <summary>
/// Implementation of dashboard route query service with caching.
/// </summary>
internal sealed class DashboardRouteQueryService : IDashboardRouteQueryService
{
    private readonly DynamicYarpConfigService _dynamicConfig;

    // Simple in-memory cache with 5-second expiration
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromSeconds(5);

    private class CacheEntry
    {
        public IReadOnlyList<DashboardRouteResponse> Routes { get; init; } = new List<DashboardRouteResponse>();
        public DateTime CachedAt { get; init; }
    }

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
        const string cacheKey = "routes";

        // Try to get from cache
        if (_cache.TryGetValue(cacheKey, out var entry) &&
            DateTime.Now - entry.CachedAt < _cacheExpiration)
        {
            return entry.Routes;
        }

        // Build routes (expensive operation)
        var routes = BuildRoutes();

        // Update cache
        _cache[cacheKey] = new CacheEntry
        {
            Routes = routes,
            CachedAt = DateTime.Now
        };

        return routes;
    }

    private IReadOnlyList<DashboardRouteResponse> BuildRoutes()
    {
        var routes = _dynamicConfig.GetRoutes();

        // Use strong-typed struct instead of anonymous object to eliminate reflection
        var clusterDest = _dynamicConfig.GetClusters()
            .ToDictionary(
                c => c.ClusterId,
                c => c.Destinations?.Select(d => new DestinationInfoEntry(d.Key, d.Value.Address)).ToList()
                    ?? new List<DestinationInfoEntry>(),
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
