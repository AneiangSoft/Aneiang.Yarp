using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Aneiang.Yarp.Services;

namespace Aneiang.Yarp.Extensions
{
    /// <summary>
    /// Aneiang.Yarp application-level extensions.
    /// </summary>
    public static class YarpApplicationExtensions
    {
        /// <summary>
        /// Manually control the gateway registration/unregistration lifecycle.
        /// <para>
        /// Registers the current service route with the remote gateway on application startup
        /// and unregisters it on shutdown.
        /// </para>
        /// <para>
        /// ! If you are using <c>AddAneiangYarpClient()</c>,
        /// <see cref="GatewayRegistrationHostedService"/> is already registered automatically —
        /// <b>do not</b> call this method!
        /// </para>
        /// <para>This method is intended for scenarios using the component-level API (<c>AddAneiangYarpGatewayClient()</c>).</para>
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
                    logger.LogWarning(ex, "Exception during registration, service continues running");
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
                    logger.LogWarning(ex, "Exception during unregistration, service shuts down normally");
                }
            });

            return app;
        }
    }
}
