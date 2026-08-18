using Aneiang.Yarp.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Aneiang.Yarp.Plugin.Retry;

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

    public void ConfigureServices(IServiceCollection services, object? pluginOptions = null) { }
    public void ConfigureMiddleware(IApplicationBuilder app) { }
    public void ConfigureProxyPipeline(IReverseProxyApplicationBuilder proxyPipeline) => proxyPipeline.UseMiddleware<RequestRetryMiddleware>();

    public void ConfigureDashboard(IPluginDashboardBuilder builder) { }

}
