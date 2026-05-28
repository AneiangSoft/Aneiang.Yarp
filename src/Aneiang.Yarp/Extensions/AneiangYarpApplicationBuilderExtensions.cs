using Aneiang.Yarp.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Aneiang.Yarp.Extensions;

/// <summary>
/// IApplicationBuilder extensions for Aneiang.Yarp core middleware.
/// </summary>
public static class AneiangYarpApplicationBuilderExtensions
{
    /// <summary>
    /// Register core gateway middleware that runs inside the YARP proxy pipeline.
    /// Call this inside the <c>MapReverseProxy</c> branch pipeline to ensure
    /// <c>IReverseProxyFeature</c> is available for route/cluster identification.
    /// </summary>
    /// <param name="builder">The IApplicationBuilder (from MapReverseProxy branch).</param>
    /// <returns>The IApplicationBuilder for chaining.</returns>
    /// <example>
    /// <code>
    /// app.MapReverseProxy(proxyPipeline =>
    /// {
    ///     proxyPipeline.UseAneiangYarp();
    /// });
    /// </code>
    /// </example>
    public static IApplicationBuilder UseAneiangYarp(this IApplicationBuilder builder)
    {
        builder.UseMiddleware<ResponseCacheMiddleware>();
        builder.UseMiddleware<BuiltinTransformMiddleware>();
        builder.UseMiddleware<MetricsMiddleware>();
        return builder;
    }
}
