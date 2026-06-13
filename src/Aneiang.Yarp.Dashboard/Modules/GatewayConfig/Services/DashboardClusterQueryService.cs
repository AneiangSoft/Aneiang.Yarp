using Aneiang.Yarp.Dashboard.Modules.Dashboard.Models;
using Aneiang.Yarp.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;

/// <summary>
/// Implementation of dashboard cluster query service.
/// Uses <see cref="IMemoryCache"/> for unified caching (shared across all query services).
/// </summary>
internal sealed class DashboardClusterQueryService : IDashboardClusterQueryService
{
    private readonly DynamicYarpConfigService _dynamicConfig;
    private readonly IMemoryCache _memoryCache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Initializes a new instance of DashboardClusterQueryService.
    /// </summary>
    /// <param name="dynamicConfig">Dynamic YARP config service.</param>
    /// <param name="memoryCache">Unified memory cache for all query services.</param>
    public DashboardClusterQueryService(
        DynamicYarpConfigService dynamicConfig,
        IMemoryCache memoryCache)
    {
        _dynamicConfig = dynamicConfig;
        _memoryCache = memoryCache;
    }

    /// <inheritdoc />
    public IReadOnlyList<DashboardClusterResponse> GetClusters()
    {
        const string cacheKey = "dashboard:clusters:query";

        if (_memoryCache.TryGetValue(cacheKey, out IReadOnlyList<DashboardClusterResponse>? cached) && cached is not null)
        {
            return cached;
        }

        var clusters = _dynamicConfig.GetClusters();

        var responses = clusters?
            .Select(cluster => DashboardClusterMapper.MapToResponse(cluster, _dynamicConfig))
            .ToList() ?? new List<DashboardClusterResponse>();

        _memoryCache.Set(cacheKey, responses, CacheDuration);
        return responses;
    }

    /// <summary>
    /// Invalidates the clusters query cache. Call after configuration changes.
    /// </summary>
    public void Invalidate() => _memoryCache.Remove("dashboard:clusters:query");
}
