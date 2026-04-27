using Aneiang.Yarp.Dashboard.Controllers;
using Microsoft.Extensions.DependencyInjection;

namespace Aneiang.Yarp.Dashboard.Extensions
{
    /// <summary>
    /// Aneiang.Yarp.Dashboard service registration extensions.
    /// </summary>
    public static class YarpServiceCollectionExtensions
    {
        /// <summary>
        /// Register Aneiang.Yarp.Dashboard services:
        /// <list type="bullet">
        ///   <item>Adds the DashboardController assembly to ApplicationParts for MVC discovery</item>
        /// </list>
        /// <para>Note: AddReverseProxy() and AddAneiangYarp() must be called before this method.</para>
        /// </summary>
        public static IServiceCollection AddAneiangYarpDashboard(this IServiceCollection services)
        {
            // Add the controller assembly so MVC can discover controllers in this library
            services.AddMvcCore().AddApplicationPart(typeof(DashboardController).Assembly);

            return services;
        }
    }
}
