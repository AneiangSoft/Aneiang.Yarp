using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Aneiang.Yarp.Services;

namespace Aneiang.Yarp.Extensions
{
    /// <summary>
    /// Aneiang.Yarp 应用级扩展。
    /// </summary>
    public static class YarpApplicationExtensions
    {
        /// <summary>
        /// 手动控制网关注册/注销时机。
        /// <para>
        /// 应用启动时自动向远程网关注册当前服务的路由，
        /// 应用关闭时自动注销路由。
        /// </para>
        /// <para>
        /// ⚠ 如果您使用了 <c>AddAneiangYarpClient()</c>，
        /// 系统已自动注册 <see cref="GatewayRegistrationHostedService"/>，
        /// <b>无需</b>再调用此方法！
        /// </para>
        /// <para>此方法适用于使用精细化 API（<c>AddAneiangYarpGatewayClient()</c>）的场景。</para>
        /// </summary>
        public static IApplicationBuilder UseAneiangYarpGateway(this IApplicationBuilder app)
        {
            var lifetime = app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>();
            var client = app.ApplicationServices.GetRequiredService<GatewayAutoRegistrationClient>();
            var logger = app.ApplicationServices.GetRequiredService<ILogger<GatewayAutoRegistrationClient>>();

            lifetime.ApplicationStarted.Register(() =>
            {
                try
                {
                    client.RegisterAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "注册阶段异常，不影响服务运行");
                }
            });

            lifetime.ApplicationStopping.Register(() =>
            {
                try
                {
                    client.UnregisterAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "注销阶段异常，不影响服务关闭");
                }
            });

            return app;
        }
    }
}
