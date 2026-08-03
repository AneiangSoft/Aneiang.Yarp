using Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;
using Aneiang.Yarp.Dashboard.Modules.Retry.Middleware;
using Aneiang.Yarp.Dashboard.Modules.Waf.Middleware;
using Aneiang.Yarp.Dashboard.Modules.RateLimit.Middleware;
using Aneiang.Yarp.Dashboard.Infrastructure.Yarp;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Plugin;

/// <summary>
/// Defines a gateway plugin that can extend the Aneiang.Yarp functionality.
/// Plugins can add middleware, services, and configuration.
/// </summary>
public enum PluginHealthStatus
{
    Healthy,
    Degraded,
    Unhealthy,
    Disabled
}

public sealed record PluginHealthProbeResult(
    PluginHealthStatus Status,
    string? Message,
    DateTimeOffset CheckedAt);

public interface IPluginHealthProbe
{
    ValueTask<PluginHealthProbeResult> CheckHealthAsync(CancellationToken cancellationToken = default);
}

public interface IGatewayPlugin
{
    /// <summary>Unique identifier for this plugin.</summary>
    string PluginId { get; }

    /// <summary>Display name of this plugin.</summary>
    string DisplayName { get; }

    /// <summary>Version of this plugin.</summary>
    string Version { get; }

    /// <summary>
    /// Static metadata used for discovery and validation. Existing plugins remain compatible
    /// through this default manifest until they provide more precise declarations.
    /// </summary>
    PluginManifest Manifest => new(
        PluginId,
        DisplayName,
        Version,
        Array.Empty<PluginScope>(),
        Array.Empty<PluginCapability>(),
        0,
        new PluginResourceRequirements(),
        Array.Empty<PluginSchemaReference>());

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

    /// <summary>
    /// Optional: Contribute Dashboard navigation items and widgets.
    /// Default implementation does nothing, keeping existing plugins compatible.
    /// </summary>
    /// <param name="builder">The dashboard builder to add items to.</param>
    void ConfigureDashboard(IPluginDashboardBuilder builder) { }
}

/// <summary>
/// Built-in plugin that wraps existing CircuitBreakerMiddleware.
/// </summary>
public class CircuitBreakerPlugin : IGatewayPlugin
{
    public string PluginId => "circuit-breaker";
    public string DisplayName => "Circuit Breaker";
    public string Version => "1.0";
    public PluginManifest Manifest => new(
        PluginId, DisplayName, Version,
        [PluginScope.Cluster],
        [PluginCapability.ProxyPipeline],
        300,
        new PluginResourceRequirements(RequestMiddleware: true),
        [new PluginSchemaReference(1, "builtin://plugins/circuit-breaker/schemas/config-v1.json", """
            {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,"properties":{"enabled":{"type":"boolean","default":true},"failureThreshold":{"type":"integer","minimum":1,"default":5},"recoveryTimeoutSeconds":{"type":"integer","minimum":1,"default":30},"halfOpenMaxAttempts":{"type":"integer","minimum":1,"default":1},"failureRatio":{"type":"number","exclusiveMinimum":0,"maximum":1,"default":0.5},"minimumThroughput":{"type":"integer","minimum":1,"default":10},"samplingDurationSeconds":{"type":"integer","minimum":1,"default":30},"failureStatusCodes":{"type":"array","items":{"type":"integer","minimum":100,"maximum":599},"uniqueItems":true,"minItems":1,"default":[500,502,503,504]}}}
            """)],
        "Cluster-scoped circuit breaker with per-destination runtime state.");

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
    public PluginManifest Manifest => new(
        PluginId, DisplayName, Version,
        [PluginScope.Route],
        [PluginCapability.ProxyPipeline],
        400,
        new PluginResourceRequirements(RequestMiddleware: true),
        [new PluginSchemaReference(1, "builtin://plugins/request-retry/schemas/config-v1.json", """
            {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,"properties":{"enabled":{"type":"boolean","default":true},"maxRetries":{"type":"integer","minimum":0,"maximum":5,"default":3},"backoffBaseMs":{"type":"integer","minimum":0,"default":100},"backoffJitterMs":{"type":"integer","minimum":0,"default":50},"timeoutSeconds":{"type":"integer","minimum":1,"maximum":300,"default":30},"retryOnExceptions":{"type":"boolean","default":true},"useDifferentDestination":{"type":"boolean","default":false},"retryNonIdempotent":{"type":"boolean","default":false},"retryOnStatusCodes":{"type":"array","items":{"type":"integer","minimum":100,"maximum":599},"uniqueItems":true,"minItems":1,"default":[502,503,504]}}}
            """)],
        "Retries failed upstream requests according to the route policy.");

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
    public PluginManifest Manifest => new(
        PluginId, DisplayName, Version,
        [PluginScope.Route],
        [PluginCapability.RequestMiddleware, PluginCapability.Dashboard],
        100,
        new PluginResourceRequirements(RequestMiddleware: true, Database: true),
        [new PluginSchemaReference(1, "builtin://plugins/waf/schemas/config-v1.json", """
            {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,"properties":{"enabled":{"type":"boolean","default":true},"enableIpCheck":{"type":"boolean","default":true},"enableRequestSizeValidation":{"type":"boolean","default":true},"enableSqlInjectionDetection":{"type":"boolean","default":true},"enableXssDetection":{"type":"boolean","default":true},"enablePathTraversalDetection":{"type":"boolean","default":true},"ipWhitelist":{"type":"array","items":{"type":"string"},"uniqueItems":true,"default":[]},"ipBlacklist":{"type":"array","items":{"type":"string"},"uniqueItems":true,"default":[]},"maxRequestBodySize":{"type":"integer","minimum":0,"default":10485760},"maxHeaderCount":{"type":"integer","minimum":0,"default":64},"maxHeaderSize":{"type":"integer","minimum":0,"default":8192}}}
            """)],
        "Route-scoped request inspection, blocking, audit, and configuration management.");

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

/// <summary>
/// Built-in plugin that wraps existing RateLimitMiddleware.
/// </summary>
public class RateLimitPlugin : IGatewayPlugin
{
    public string PluginId => "rate-limit";
    public string DisplayName => "Rate Limiting";
    public string Version => "1.0";
    public PluginManifest Manifest => new(
        PluginId, DisplayName, Version,
        [PluginScope.Route],
        [PluginCapability.ProxyPipeline],
        200,
        new PluginResourceRequirements(RequestMiddleware: true),
        [new PluginSchemaReference(1, "builtin://plugins/rate-limit/schemas/config-v1.json", """
            {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,"properties":{"enabled":{"type":"boolean","default":true},"algorithm":{"type":"string","enum":["FixedWindow","SlidingWindow","TokenBucket","Concurrency"],"default":"FixedWindow"},"permitLimit":{"type":"integer","minimum":1,"default":100},"window":{"type":"string","minLength":1,"default":"1m"},"queueLimit":{"type":"integer","minimum":0,"default":0},"partitionKey":{"type":"string","enum":["IpAddress","UserId","Route","Global"],"default":"IpAddress"},"segmentsPerWindow":{"type":"integer","minimum":2,"maximum":100,"default":4},"tokenLimit":{"type":"integer","minimum":1,"default":100},"tokensPerPeriod":{"type":"integer","minimum":1,"default":100},"replenishmentPeriod":{"type":"string","minLength":1,"default":"1s"}}}
            """)],
        "Route-scoped request throughput limiting with selectable algorithms.");

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

/// <summary>
/// Built-in route plugin that captures proxy request and response logs.
/// </summary>
public sealed class ProxyLogPlugin : IGatewayPlugin
{
    public string PluginId => "proxy-log";
    public string DisplayName => "Proxy Log";
    public string Version => "1.0";
    public PluginManifest Manifest => new(
        PluginId, DisplayName, Version,
        [PluginScope.Route],
        [PluginCapability.RequestMiddleware, PluginCapability.Dashboard, PluginCapability.BackgroundService],
        500,
        new PluginResourceRequirements(RequestMiddleware: true, BackgroundServices: true, Database: true),
        [new PluginSchemaReference(1, "builtin://plugins/proxy-log/schemas/config-v1.json", """
            {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,"properties":{"captureRequestHeaders":{"type":"boolean","default":true},"captureResponseHeaders":{"type":"boolean","default":true},"requestBodyCaptureEnabled":{"type":"boolean","default":false},"responseBodyCaptureEnabled":{"type":"boolean","default":false},"maxBodyLength":{"type":"integer","minimum":0,"maximum":1048576,"default":8192},"maxBodyBufferBytes":{"type":"integer","minimum":0,"maximum":1048576,"default":65536},"errorsOnly":{"type":"boolean","default":false},"samplingEnabled":{"type":"boolean","default":false},"samplingRate":{"type":"number","minimum":0,"maximum":1,"default":1}}}
            """)],
        "Route-scoped proxy metadata and optional body capture with background persistence.");

    public void ConfigureServices(IServiceCollection services, object? pluginOptions = null)
    {
        // Proxy log services are registered by the host only when this plugin is active.
    }

    public void ConfigureMiddleware(IApplicationBuilder app)
    {
    }

    public void ConfigureProxyPipeline(IReverseProxyApplicationBuilder proxyPipeline)
    {
        proxyPipeline.UseMiddleware<YarpRequestCaptureMiddleware>();
    }
}
