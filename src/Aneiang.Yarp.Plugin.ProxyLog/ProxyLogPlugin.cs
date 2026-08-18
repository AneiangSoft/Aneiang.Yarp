using Aneiang.Yarp.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Aneiang.Yarp.Plugin.ProxyLog;

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

    public void ConfigureServices(IServiceCollection services, object? pluginOptions = null) { }
    public void ConfigureMiddleware(IApplicationBuilder app) { }
    public void ConfigureProxyPipeline(IReverseProxyApplicationBuilder proxyPipeline) => proxyPipeline.UseMiddleware<YarpRequestCaptureMiddleware>();

    public void ConfigureDashboard(IPluginDashboardBuilder builder) { }

}
