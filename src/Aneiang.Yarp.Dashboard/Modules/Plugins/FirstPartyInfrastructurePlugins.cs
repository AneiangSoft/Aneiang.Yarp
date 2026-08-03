using Aneiang.Yarp.Dashboard.Modules.Plugins.Runtime;
using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Aneiang.Yarp.Dashboard.Modules.Plugins;

public sealed class ResponseCachePlugin : IGatewayPlugin
{
    public string PluginId => "response-cache";
    public string DisplayName => "Response Cache";
    public string Version => "1.0";
    public PluginManifest Manifest => new(PluginId, DisplayName, Version, [PluginScope.Route], [PluginCapability.ProxyPipeline], 450,
        new PluginResourceRequirements(RequestMiddleware: true),
        [new PluginSchemaReference(1, "builtin://plugins/response-cache/schemas/config-v1.json", """
        {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,"required":["enabled"],"properties":{"enabled":{"type":"boolean","default":true},"ttlSeconds":{"type":"integer","minimum":1,"maximum":86400,"default":60},"maxBodyBytes":{"type":"integer","minimum":1024,"maximum":10485760,"default":1048576},"varyByQuery":{"type":"boolean","default":true},"varyHeaders":{"type":"array","items":{"type":"string","minLength":1},"uniqueItems":true,"default":[]},"cacheStatusCodes":{"type":"array","items":{"type":"integer","minimum":200,"maximum":599},"uniqueItems":true,"default":[200]}}}
        """)], "Route-scoped bounded in-memory HTTP response cache.");
    public void ConfigureServices(IServiceCollection services, object? pluginOptions = null) { }
    public void ConfigureMiddleware(IApplicationBuilder app) { }
    public void ConfigureProxyPipeline(IReverseProxyApplicationBuilder proxyPipeline) => proxyPipeline.UseMiddleware<ResponseCacheMiddleware>();
}

public sealed class DistributedRateLimitPlugin : IGatewayPlugin
{
    public string PluginId => "distributed-rate-limit";
    public string DisplayName => "Distributed Rate Limit";
    public string Version => "1.0";
    public PluginManifest Manifest => new(PluginId, DisplayName, Version, [PluginScope.Route], [PluginCapability.ProxyPipeline], 190,
        new PluginResourceRequirements(RequestMiddleware: true),
        [new PluginSchemaReference(1, "builtin://plugins/distributed-rate-limit/schemas/config-v1.json", """
        {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,"required":["enabled","permitLimit","windowSeconds"],"properties":{"enabled":{"type":"boolean","default":true},"permitLimit":{"type":"integer","minimum":1,"default":100},"windowSeconds":{"type":"integer","minimum":1,"maximum":86400,"default":60},"backend":{"type":"string","enum":["Memory","Sqlite"],"default":"Memory"},"partitionKey":{"type":"string","enum":["IpAddress","UserId","Route","Global"],"default":"IpAddress"},"userHeader":{"type":"string","minLength":1,"default":"X-User-Id"}}}
        """)], "Route-scoped shared fixed-window limiter. The first-party in-memory backend is process-wide and requires no external connection.");
    public void ConfigureServices(IServiceCollection services, object? pluginOptions = null) { }
    public void ConfigureMiddleware(IApplicationBuilder app) { }
    public void ConfigureProxyPipeline(IReverseProxyApplicationBuilder proxyPipeline) => proxyPipeline.UseMiddleware<DistributedRateLimitMiddleware>();
}

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
}

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
}
