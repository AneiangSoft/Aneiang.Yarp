using Aneiang.Yarp.Dashboard.Modules.Retry.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Plugin;

/// <summary>
/// Built-in plugin that wraps existing RequestRetryMiddleware.
/// </summary>
public class RequestRetryPlugin : IGatewayPlugin
{
    public string PluginId => "request-retry";
    public string DisplayName => "Request Retry";
    public string Version => "1.0";
    public string Description => "Automatically retries failed proxy requests with configurable backoff strategy.";

    public void ConfigureServices(IServiceCollection services, object? pluginOptions = null)
    {
        // Retry services are already registered by Dashboard
    }

    public void ConfigureMiddleware(IApplicationBuilder app)
    {
        // Retry runs inside MapReverseProxy via ConfigureProxyPipeline
    }

    public void ConfigureProxyPipeline(IReverseProxyApplicationBuilder proxyPipeline)
    {
        proxyPipeline.UseMiddleware<RequestRetryMiddleware>();
    }
}
