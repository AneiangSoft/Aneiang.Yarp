using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Dashboard.Services;
using Aneiang.Yarp.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Extensions;

/// <summary>
/// IApplicationBuilder extensions for Aneiang.Yarp.Dashboard.
/// </summary>
public static class DashboardApplicationBuilderExtensions
{
    /// <summary>
    /// Registers the YARP request capture middleware and optional feature middleware.
    /// Captures incoming request path/method before YARP processes it.
    /// Must be called after UseRouting() and before MapReverseProxy().
    /// </summary>
    /// <param name="app">The IApplicationBuilder.</param>
    /// <returns>The IApplicationBuilder for chaining.</returns>
    public static IApplicationBuilder UseAneiangYarpDashboard(this IApplicationBuilder app)
    {
        app.UseStaticFiles();
        app.UseMiddleware<BuiltinTransformMiddleware>();
        app.UseMiddleware<YarpRequestCaptureMiddleware>();
        return app;
    }
}
