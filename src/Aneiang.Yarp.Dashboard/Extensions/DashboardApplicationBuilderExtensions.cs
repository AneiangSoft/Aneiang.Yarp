using Aneiang.Yarp.Dashboard.Services;
using Microsoft.AspNetCore.Builder;

namespace Aneiang.Yarp.Dashboard.Extensions;

/// <summary>
/// IApplicationBuilder extensions for Aneiang.Yarp.Dashboard / Dashboard 使用中间件的扩展.
/// </summary>
public static class DashboardApplicationBuilderExtensions
{
    /// <summary>
    /// Registers the YARP request capture middleware.
    /// Captures incoming request path/method before YARP processes it.
    /// Must be called after UseRouting() and before MapReverseProxy().
    /// 注册 YARP 请求捕获中间件，在 YARP 处理前捕获请求路径和方法。需在 UseRouting() 之后、MapReverseProxy() 之前调用.
    /// </summary>
    /// <param name="app">The IApplicationBuilder.</param>
    /// <returns>The IApplicationBuilder for chaining.</returns>
    public static IApplicationBuilder UseAneiangYarpDashboard(this IApplicationBuilder app)
    {
        return app.UseMiddleware<YarpRequestCaptureMiddleware>();
    }
}
