using Aneiang.Yarp.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Aneiang.Yarp.Plugin.Metrics;

public sealed class TrafficMetricsPlugin : IGatewayPlugin
{
    public string PluginId => "traffic-metrics";
    public string DisplayName => "Traffic Metrics";
    public string Version => "1.0";
    public PluginManifest Manifest => new(PluginId, DisplayName, Version, [PluginScope.Route], [PluginCapability.ProxyPipeline, PluginCapability.Dashboard], 600,
        new PluginResourceRequirements(RequestMiddleware: true),
        [new PluginSchemaReference(1, "builtin://plugins/traffic-metrics/schemas/config-v1.json", """
        {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,"properties":{"enabled":{"type":"boolean","default":true},"includeRequestBytes":{"type":"boolean","default":true},"includeResponseBytes":{"type":"boolean","default":true}}}
        """)], "Route-scoped request, error, latency, and byte metrics.");
    public void ConfigureServices(IServiceCollection services, object? pluginOptions = null) { }
    public void ConfigureMiddleware(IApplicationBuilder app) { }
    public void ConfigureProxyPipeline(IReverseProxyApplicationBuilder proxyPipeline) => proxyPipeline.UseMiddleware<PluginMetricsMiddleware>();
    public void ConfigureDashboard(IPluginDashboardBuilder builder) { }
}

public sealed class ClusterMetricsPlugin : IGatewayPlugin
{
    public string PluginId => "cluster-metrics";
    public string DisplayName => "Cluster Metrics";
    public string Version => "1.0";
    public PluginManifest Manifest => new(PluginId, DisplayName, Version, [PluginScope.Cluster], [PluginCapability.ProxyPipeline, PluginCapability.Dashboard], 610,
        new PluginResourceRequirements(RequestMiddleware: true),
        [new PluginSchemaReference(1, "builtin://plugins/cluster-metrics/schemas/config-v1.json", """
        {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,"properties":{"enabled":{"type":"boolean","default":true},"includeDestination":{"type":"boolean","default":true}}}
        """)], "Cluster-scoped request, error, latency, and destination metrics.");
    public void ConfigureServices(IServiceCollection services, object? pluginOptions = null) { }
    public void ConfigureMiddleware(IApplicationBuilder app) { }
    public void ConfigureProxyPipeline(IReverseProxyApplicationBuilder proxyPipeline) { }
    public void ConfigureDashboard(IPluginDashboardBuilder builder) { }
}
