using Aneiang.Yarp.Dashboard.Modules.RateLimit.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Plugin;

/// <summary>
/// Built-in plugin that wraps existing RateLimitMiddleware.
/// </summary>
public class RateLimitPlugin : IGatewayPlugin
{
    public string PluginId => "rate-limit";
    public string DisplayName => "Rate Limiting";
    public string Version => "1.0";
    public string Description => "Rate limiting to protect the gateway from being overloaded.";

    public void ConfigureServices(IServiceCollection services, object? pluginOptions = null)
    {
    }

    public void ConfigureMiddleware(IApplicationBuilder app)
    {
    }

    public void ConfigureProxyPipeline(IReverseProxyApplicationBuilder proxyPipeline)
    {
        proxyPipeline.UseMiddleware<RateLimitMiddleware>();
    }
}
