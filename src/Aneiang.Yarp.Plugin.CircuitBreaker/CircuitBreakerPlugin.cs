using Aneiang.Yarp.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Aneiang.Yarp.Plugin.CircuitBreaker;

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

    public void ConfigureServices(IServiceCollection services, object? pluginOptions = null) { }
    public void ConfigureMiddleware(IApplicationBuilder app) { }
    public void ConfigureProxyPipeline(IReverseProxyApplicationBuilder proxyPipeline) => proxyPipeline.UseMiddleware<CircuitBreakerMiddleware>();

    public void ConfigureDashboard(IPluginDashboardBuilder builder) =>
        builder.AddNavItem(new PluginNavItem(PluginId, "menu.circuits", "/circuits", "bi-lightning-charge", 300));

}
