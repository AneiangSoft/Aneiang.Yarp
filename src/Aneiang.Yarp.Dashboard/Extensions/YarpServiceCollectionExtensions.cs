using Aneiang.Yarp.Dashboard.Controllers;
using Aneiang.Yarp.Dashboard.Services;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Dashboard.Extensions
{
    /// <summary>
    /// Aneiang.Yarp 服务注册扩展方法
    /// </summary>
    public static class YarpServiceCollectionExtensions
    {
        /// <summary>
        /// 注册 Aneiang.Yarp 所需的服务：
        /// <list type="bullet">
        ///   <item><see cref="InMemoryConfigProvider"/>（单例）</item>
        ///   <item><see cref="IProxyConfigProvider"/>（指向同一实例）</item>
        ///   <item><see cref="DynamicYarpConfigService"/>（单例）</item>
        ///   <item>DashboardController / GatewayConfigController 所在程序集加入 ApplicationParts</item>
        /// </list>
        /// <para>注意：调用此方法前必须先注册 YARP（services.AddReverseProxy()）</para>
        /// </summary>
        public static IServiceCollection AddAneiangYarp(this IServiceCollection services)
        {
            // 注册 InMemoryConfigProvider，用于运行时动态添加路由/集群
            var inMemoryConfigProvider = new InMemoryConfigProvider(
                Array.Empty<RouteConfig>(),
                Array.Empty<ClusterConfig>()
            );
            services.AddSingleton<IProxyConfigProvider>(inMemoryConfigProvider);
            services.AddSingleton(inMemoryConfigProvider);
            services.AddSingleton<DynamicYarpConfigService>();

            // 添加 Controller 所在程序集，使 MVC 可发现本类库中的控制器
            services.AddMvcCore().AddApplicationPart(typeof(DashboardController).Assembly);

            return services;
        }
    }
}
