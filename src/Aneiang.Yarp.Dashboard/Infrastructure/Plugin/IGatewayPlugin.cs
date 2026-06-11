using Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;
using Aneiang.Yarp.Dashboard.Modules.Retry.Middleware;
using Aneiang.Yarp.Dashboard.Modules.Waf.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Plugin;

/// <summary>
/// Defines a gateway plugin that can extend the Aneiang.Yarp functionality.
/// Plugins can add middleware, services, and configuration.
/// </summary>
public interface IGatewayPlugin
{
    /// <summary>Unique identifier for this plugin.</summary>
    string PluginId { get; }

    /// <summary>Display name of this plugin.</summary>
    string DisplayName { get; }

    /// <summary>Version of this plugin.</summary>
    string Version { get; }

    /// <summary>
    /// Configure plugin services during DI setup.
    /// Use this to register plugin-specific services, options, and middleware.
    /// </summary>
    /// <param name="services">IServiceCollection to register services.</param>
    /// <param name="pluginOptions">JSON-serializable plugin options loaded from config.</param>
    void ConfigureServices(IServiceCollection services, object? pluginOptions = null);

    /// <summary>
    /// Configure plugin middleware in the ASP.NET Core pipeline.
    /// Called after UseRouting() but before MapReverseProxy().
    /// </summary>
    /// <param name="app">IApplicationBuilder to register middleware.</param>
    void ConfigureMiddleware(IApplicationBuilder app);

    /// <summary>
    /// Optional: Configure proxy pipeline middleware (inside MapReverseProxy).
    /// Only called if the plugin wants to add middleware to the proxy branch.
    /// </summary>
    /// <param name="proxyPipeline">IReverseProxyApplicationBuilder for proxy pipeline middleware.</param>
    void ConfigureProxyPipeline(IReverseProxyApplicationBuilder proxyPipeline);
}

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

/// <summary>
/// Built-in plugin that wraps existing RequestRetryMiddleware.
/// </summary>
public class RequestRetryPlugin : IGatewayPlugin
{
    public string PluginId => "request-retry";
    public string DisplayName => "Request Retry";
    public string Version => "1.0";

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

/// <summary>
/// Built-in plugin that wraps existing WafMiddleware.
/// </summary>
public class WafPlugin : IGatewayPlugin
{
    public string PluginId => "waf";
    public string DisplayName => "Web Application Firewall";
    public string Version => "1.0";

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
        // WAF runs on main pipeline
    }
}
