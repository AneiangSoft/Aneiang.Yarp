using Aneiang.Yarp.Dashboard.Extensions;
using System.Text.Json;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Aneiang.Yarp.Extensions;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage.Entities;
using Aneiang.Yarp.Storage.Sqlite;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:5302");

var mode = args.FirstOrDefault(x => x.StartsWith("--mode=", StringComparison.OrdinalIgnoreCase))?[7..] ?? "full";
var wafEnabled = mode is "waf" or "waf-attack";
var retryEnabled = mode.StartsWith("retry", StringComparison.OrdinalIgnoreCase);
var circuitEnabled = mode.StartsWith("circuit", StringComparison.OrdinalIgnoreCase);

builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Gateway:Storage:Sqlite:ConnectionString"] = $"Data Source=performance-{mode}.db",
    ["Gateway:Deployment:Mode"] = "ProxyOnly",
    ["Gateway:Deployment:HealthCheck:Enabled"] = "false",
    ["Gateway:Dashboard:AuthMode"] = "None",
    ["Gateway:Dashboard:ProxyLog:LogPersistenceEnabled"] = (mode == "log-sqlite").ToString(),
    ["Gateway:Dashboard:ProxyLog:EnableProxyRequestBodyCapture"] = (mode is "log-request" or "log-bodies" or "log-sqlite").ToString(),
    ["Gateway:Dashboard:ProxyLog:EnableProxyResponseBodyCapture"] = (mode is "log-response" or "log-bodies" or "log-sqlite").ToString(),
    ["Gateway:Dashboard:ProxyLog:LogMaxBodyLength"] = (256 * 1024).ToString(),
    ["Gateway:Dashboard:ProxyLog:LogMaxBodyBufferBytes"] = (256 * 1024).ToString(),
    ["ReverseProxy:Routes:perf:ClusterId"] = "backend",
    ["ReverseProxy:Routes:perf:Match:Path"] = "/api/perf/{**catch-all}",
    ["ReverseProxy:Clusters:backend:Destinations:backend:Address"] = "http://127.0.0.1:5300"
});

var coreOnly = mode == "core";
var storageOnly = mode == "storage";
var servicesOnly = mode == "services";
var nativeHost = mode == "host-native";

if (nativeHost)
{
    builder.Services.AddReverseProxy().LoadFromMemory(
        [new Yarp.ReverseProxy.Configuration.RouteConfig
        {
            RouteId = "perf",
            ClusterId = "backend",
            Match = new Yarp.ReverseProxy.Configuration.RouteMatch { Path = "/api/perf/{**catch-all}" }
        }],
        [new Yarp.ReverseProxy.Configuration.ClusterConfig
        {
            ClusterId = "backend",
            Destinations = new Dictionary<string, Yarp.ReverseProxy.Configuration.DestinationConfig>
            {
                ["backend"] = new() { Address = "http://127.0.0.1:5300" }
            }
        }]);
}
else
{
    builder.Services.AddAneiangYarp(enableRegistration: false);
    if (!coreOnly)
    {
        builder.Services.AddAneiangStorage();
        if (!storageOnly) builder.Services.AddAneiangYarpDashboard();
    }
}

var app = builder.Build();
app.UseRouting();
if (coreOnly || storageOnly || servicesOnly || nativeHost)
{
    app.MapReverseProxy();
}
else
{
    var useFullPipeline = mode != "minimal";
    app.UseAneiangYarpDashboard(new DashboardApplicationBuilderExtensions.DashboardUseOptions
    {
        AutoUseMiddleware = useFullPipeline,
        UseDeploymentMiddleware = false,
        UseProxyRequestCapture = useFullPipeline,
        UseWaf = wafEnabled,
        UseBuiltInProxyPipeline = useFullPipeline,
        AutoUseCors = false,
        AutoUseAuthorization = false
    });
}
app.MapGet("/health", () => Results.Ok());

await app.StartAsync();
if (retryEnabled || circuitEnabled)
{
    var configService = app.Services.GetRequiredService<IDynamicYarpConfigService>();
    var mutations = app.Services.GetRequiredService<IPluginBindingMutationService>();
    var manifests = app.Services.GetRequiredService<IGatewayPluginManager>();
    var dynamicConfig = configService.GetDynamicConfig();
    if (retryEnabled)
    {
        var route = dynamicConfig?.Routes.First(item => item.Config.RouteId == "perf")
            ?? throw new InvalidOperationException("Performance route is unavailable.");
        var manifest = manifests.GetManifest("request-retry")
            ?? throw new InvalidOperationException("Retry plugin manifest is unavailable.");
        await mutations.UpsertAsync(new PluginBindingEntity
        {
            Id = $"route:{route.RouteUid}:request-retry",
            PluginId = "request-retry",
            PluginVersion = manifest.Version,
            Scope = PluginBindingScope.Route,
            ScopeId = "perf",
            RouteUid = route.RouteUid,
            ConfigJson = JsonSerializer.Serialize(new
            {
                enabled = true,
                maxRetries = 2,
                backoffBaseMs = 1,
                backoffJitterMs = 1,
                statusCodes = new[] { 502, 503, 504 }
            }),
            SchemaVersion = manifest.Schemas.FirstOrDefault()?.Version ?? 1
        });
    }
    if (circuitEnabled)
    {
        var cluster = dynamicConfig?.Clusters.First(item => item.Config.ClusterId == "backend")
            ?? throw new InvalidOperationException("Performance cluster is unavailable.");
        var manifest = manifests.GetManifest("circuit-breaker")
            ?? throw new InvalidOperationException("Circuit breaker plugin manifest is unavailable.");
        await mutations.UpsertAsync(new PluginBindingEntity
        {
            Id = $"cluster:{cluster.ClusterUid}:circuit-breaker",
            PluginId = "circuit-breaker",
            PluginVersion = manifest.Version,
            Scope = PluginBindingScope.Cluster,
            ScopeId = "backend",
            ClusterUid = cluster.ClusterUid,
            ConfigJson = JsonSerializer.Serialize(new
            {
                enabled = true,
                failureThreshold = 3,
                recoveryTimeoutSeconds = 2,
                halfOpenMaxAttempts = 1,
                failureRatio = 0.5,
                minimumThroughput = 3,
                samplingDurationSeconds = 30,
                failureStatusCodes = new[] { 500, 502, 503, 504 }
            }),
            SchemaVersion = manifest.Schemas.FirstOrDefault()?.Version ?? 1
        });
    }
}
await app.WaitForShutdownAsync();
