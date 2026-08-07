using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Aneiang.Yarp.Plugins;

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
