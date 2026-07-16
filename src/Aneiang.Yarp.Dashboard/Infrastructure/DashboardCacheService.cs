using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.OutputCaching;

namespace Aneiang.Yarp.Dashboard.Infrastructure;

/// <summary>
/// Dashboard caching service for optimized API response delivery.
/// Provides multi-tier caching: OutputCache for HTTP responses, In-Memory cache for computed data.
/// </summary>
public class DashboardCacheService
{
    private readonly IMemoryCache _memoryCache;
    private readonly IOutputCacheStore? _outputCache;

    // Cache key prefixes
    private const string StatsKey = "dashboard:stats:";
    private const string RoutesKey = "dashboard:routes:";
    private const string ClustersKey = "dashboard:clusters:";
    private const string InfoKey = "dashboard:info:";

    // Cache durations
    private static readonly TimeSpan StatsCacheDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RoutesCacheDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ClustersCacheDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InfoCacheDuration = TimeSpan.FromMinutes(1);

    public DashboardCacheService(IMemoryCache memoryCache, IOutputCacheStore? outputCache = null)
    {
        _memoryCache = memoryCache;
        _outputCache = outputCache;
    }

    /// <summary>
    /// Gets or creates cached statistics data.
    /// </summary>
    public async Task<T?> GetOrCreateStatsAsync<T>(Func<Task<T>> factory, CancellationToken ct = default)
    {
        var key = StatsKey + "data";
        if (_memoryCache.TryGetValue(key, out T? cached))
            return cached;

        var result = await factory();
        _memoryCache.Set(key, result, StatsCacheDuration);
        return result;
    }

    /// <summary>
    /// Gets or creates cached routes data.
    /// </summary>
    public async Task<T?> GetOrCreateRoutesAsync<T>(Func<Task<T>> factory, CancellationToken ct = default)
    {
        var key = RoutesKey + "data";
        if (_memoryCache.TryGetValue(key, out T? cached))
            return cached;

        var result = await factory();
        _memoryCache.Set(key, result, RoutesCacheDuration);
        return result;
    }

    /// <summary>
    /// Gets or creates cached clusters data.
    /// </summary>
    public async Task<T?> GetOrCreateClustersAsync<T>(Func<Task<T>> factory, CancellationToken ct = default)
    {
        var key = ClustersKey + "data";
        if (_memoryCache.TryGetValue(key, out T? cached))
            return cached;

        var result = await factory();
        _memoryCache.Set(key, result, ClustersCacheDuration);
        return result;
    }

    /// <summary>
    /// Gets or creates cached dashboard info.
    /// </summary>
    public async Task<T?> GetOrCreateInfoAsync<T>(Func<Task<T>> factory, CancellationToken ct = default)
    {
        var key = InfoKey + "data";
        if (_memoryCache.TryGetValue(key, out T? cached))
            return cached;

        var result = await factory();
        _memoryCache.Set(key, result, InfoCacheDuration);
        return result;
    }

    /// <summary>
    /// Invalidates all dashboard caches. Call when configuration changes.
    /// </summary>
    public void InvalidateAll()
    {
        _memoryCache.Remove(StatsKey + "data");
        _memoryCache.Remove(RoutesKey + "data");
        _memoryCache.Remove(ClustersKey + "data");
    }

    /// <summary>
    /// Invalidates statistics cache.
    /// </summary>
    public void InvalidateStats()
    {
        _memoryCache.Remove(StatsKey + "data");
    }

    /// <summary>
    /// Invalidates routes cache.
    /// </summary>
    public void InvalidateRoutes()
    {
        _memoryCache.Remove(RoutesKey + "data");
    }

    /// <summary>
    /// Invalidates clusters cache.
    /// </summary>
    public void InvalidateClusters()
    {
        _memoryCache.Remove(ClustersKey + "data");
    }
}

