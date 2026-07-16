using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;

namespace Aneiang.Yarp.Dashboard.Infrastructure;

/// <summary>
/// Extension methods for registering dashboard caching services.
/// </summary>
public static class DashboardCacheExtensions
{
    /// <summary>
    /// Adds dashboard caching services including OutputCache.
    /// Note: <c>AddMemoryCache()</c> is already called by <c>AddAneiangYarpDashboard</c>
    /// and should NOT be called again here.
    /// </summary>
    public static IServiceCollection AddDashboardCaching(this IServiceCollection services)
    {
        // Add output caching for API responses
        services.AddOutputCache(options =>
        {
            options.AddBasePolicy(builder =>
                builder.Expire(TimeSpan.FromSeconds(10)));

            options.AddPolicy("ShortCache", builder =>
                builder.Expire(TimeSpan.FromSeconds(5)));

            options.AddPolicy("LongCache", builder =>
                builder.Expire(TimeSpan.FromMinutes(1)));
        });

        // Register dashboard cache service
        services.AddSingleton<DashboardCacheService>();

        return services;
    }

    /// <summary>
    /// Configures dashboard output caching middleware.
    /// </summary>
    public static IApplicationBuilder UseDashboardOutputCache(this IApplicationBuilder app)
    {
        app.UseOutputCache();
        return app;
    }
}
