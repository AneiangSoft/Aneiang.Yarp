using Aneiang.Yarp.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Aneiang.Yarp.Plugin.ServiceDiscovery;

public sealed class HttpServiceDiscoveryPlugin : IGatewayPlugin
{
    public string PluginId => "service-discovery";
    public string DisplayName => "Service Discovery";
    public string Version => "1.0";
    public PluginManifest Manifest => new(PluginId, DisplayName, Version, [PluginScope.Cluster], [PluginCapability.BackgroundService, PluginCapability.Dashboard], 50,
        new PluginResourceRequirements(BackgroundServices: true, NetworkConnections: true),
        [new PluginSchemaReference(1, "builtin://plugins/service-discovery/schemas/config-v1.json", """
        {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,"required":["enabled","mode"],"properties":{"enabled":{"type":"boolean","default":true},"mode":{"type":"string","enum":["Static","HttpJson","Consul","Nacos","Eureka","Kubernetes"],"default":"Static"},"staticEndpoints":{"type":"array","items":{"type":"string","format":"uri"},"uniqueItems":true,"default":[]},"endpoint":{"type":"string","format":"uri"},"serviceName":{"type":"string","minLength":1},"namespace":{"type":"string","default":"default"},"scheme":{"type":"string","enum":["http","https"],"default":"http"},"refreshSeconds":{"type":"integer","minimum":5,"maximum":3600,"default":30},"requestTimeoutSeconds":{"type":"integer","minimum":1,"maximum":60,"default":5}}}
        """)], "Cluster-scoped static or HTTP JSON endpoint discovery; no network connection is opened unless a cluster binding exists.");
    public void ConfigureServices(IServiceCollection services, object? pluginOptions = null) { }
    public void ConfigureMiddleware(IApplicationBuilder app) { }
    public void ConfigureProxyPipeline(IReverseProxyApplicationBuilder proxyPipeline) { }

    public void ConfigureDashboard(IPluginDashboardBuilder builder) { }
}
