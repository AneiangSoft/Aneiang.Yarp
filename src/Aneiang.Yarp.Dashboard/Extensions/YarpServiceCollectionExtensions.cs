using Aneiang.Yarp.Dashboard.Controllers;
using Microsoft.Extensions.DependencyInjection;

namespace Aneiang.Yarp.Dashboard.Extensions
{
    /// <summary>
    /// Aneiang.Yarp.Dashboard 服务注册扩展方法
    /// </summary>
    public static class YarpServiceCollectionExtensions
    {
        /// <summary>
        /// 注册 Aneiang.Yarp.Dashboard 所需的服务：
        /// <list type="bullet">
        ///   <item>DashboardController 所在程序集加入 ApplicationParts</item>
        /// </list>
        /// <para>注意：调用此方法前必须先注册 YARP（services.AddReverseProxy()）和 Aneiang.Yarp（services.AddAneiangYarp()）</para>
        /// </summary>
        public static IServiceCollection AddAneiangYarpDashboard(this IServiceCollection services)
        {
            // 添加 Controller 所在程序集，使 MVC 可发现本类库中的控制器
            services.AddMvcCore().AddApplicationPart(typeof(DashboardController).Assembly);

            return services;
        }
    }
}
