using Aneiang.Yarp.Dashboard.Modules.Waf.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Plugin;

/// <summary>
/// Built-in plugin that wraps existing WafMiddleware.
/// </summary>
public class WafPlugin : IGatewayPlugin
{
    public string PluginId => "waf";
    public string DisplayName => "Web Application Firewall";
    public string Version => "1.0";
    public string Description => "Web Application Firewall: blocks SQL injection, XSS, path traversal, and other attacks.";

    public void ConfigureServices(IServiceCollection services, object? pluginOptions = null)
    {
        // WAF services are already registered by Dashboard
    }

    public void ConfigureMiddleware(IApplicationBuilder app)
    {
        app.UseMiddleware<WafMiddleware>();
    }

    public void ConfigureProxyPipeline(IReverseProxyApplicationBuilder proxyPipeline)
    {
        // WAF runs on main pipeline, not inside MapReverseProxy
    }
}
