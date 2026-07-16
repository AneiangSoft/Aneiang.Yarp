using Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Plugin;

/// <summary>
/// Built-in plugin that wraps existing CircuitBreakerMiddleware.
/// </summary>
public class CircuitBreakerPlugin : IGatewayPlugin
{
    public string PluginId => "circuit-breaker";
    public string DisplayName => "Circuit Breaker";
    public string Version => "1.0";

    public void ConfigureServices(IServiceCollection services, object? pluginOptions = null)
    {
        // Circuit breaker services are already registered by Dashboard
    }

    public void ConfigureMiddleware(IApplicationBuilder app)
    {
        // Circuit breaker runs inside MapReverseProxy via ConfigureProxyPipeline
    }

    public void ConfigureProxyPipeline(IReverseProxyApplicationBuilder proxyPipeline)
    {
        proxyPipeline.UseMiddleware<CircuitBreakerMiddleware>();
    }
}
