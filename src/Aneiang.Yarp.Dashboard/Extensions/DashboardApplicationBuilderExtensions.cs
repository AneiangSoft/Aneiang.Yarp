using Aneiang.Yarp.Dashboard.Services;
using Microsoft.AspNetCore.Builder;

namespace Aneiang.Yarp.Dashboard.Extensions;

/// <summary>
/// IApplicationBuilder extensions for Aneiang.Yarp.Dashboard.
/// </summary>
public static class DashboardApplicationBuilderExtensions
{
    /// <summary>
    /// Registers the YARP request capture middleware.
    /// Captures incoming request path/method before YARP processes it.
    /// Must be called after UseRouting() and before MapReverseProxy().
    /// </summary>
    /// <param name="app">The IApplicationBuilder.</param>
    /// <returns>The IApplicationBuilder for chaining.</returns>
    public static IApplicationBuilder UseAneiangYarpDashboard(this IApplicationBuilder app)
    {
        return app.UseMiddleware<YarpRequestCaptureMiddleware>();
    }
}
