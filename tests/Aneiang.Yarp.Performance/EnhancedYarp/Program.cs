using Aneiang.Yarp.Dashboard.Extensions;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Extensions;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
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
var logEnabled = mode.StartsWith("log-", StringComparison.OrdinalIgnoreCase);
var rateAlgorithm = mode switch
{
    "rate-sliding" => "SlidingWindow",
    "rate-token" => "TokenBucket",
    "rate-concurrency" => "Concurrency",
    _ => "FixedWindow"
};
var rateEnabled = mode.StartsWith("rate-", StringComparison.OrdinalIgnoreCase);
var retryEnabled = mode.StartsWith("retry", StringComparison.OrdinalIgnoreCase);
var circuitEnabled = mode.StartsWith("circuit", StringComparison.OrdinalIgnoreCase);

builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Gateway:Storage:Sqlite:ConnectionString"] = $"Data Source=performance-{mode}.db",
    ["Gateway:Deployment:Mode"] = "ProxyOnly",
    ["Gateway:Deployment:HealthCheck:Enabled"] = "false",
    ["Gateway:Dashboard:AuthMode"] = "None",
    ["Gateway:Dashboard:EnableProxyLogging"] = logEnabled.ToString(),
    ["Gateway:Dashboard:ProxyLog:EnableProxyLogging"] = logEnabled.ToString(),
    ["Gateway:Dashboard:ProxyLog:LogPersistenceEnabled"] = (mode == "log-sqlite").ToString(),
    ["Gateway:Dashboard:ProxyLog:EnableProxyRequestBodyCapture"] = (mode is "log-request" or "log-bodies" or "log-sqlite").ToString(),
    ["Gateway:Dashboard:ProxyLog:EnableProxyResponseBodyCapture"] = (mode is "log-response" or "log-bodies" or "log-sqlite").ToString(),
    ["Gateway:Dashboard:ProxyLog:LogMaxBodyLength"] = (256 * 1024).ToString(),
    ["Gateway:Dashboard:ProxyLog:LogMaxBodyBufferBytes"] = (256 * 1024).ToString(),
    ["Gateway:Dashboard:Waf:Enabled"] = wafEnabled.ToString(),
    ["Gateway:Dashboard:CircuitBreaker:Enabled"] = circuitEnabled.ToString(),
    ["Gateway:Dashboard:Retry:Enabled"] = retryEnabled.ToString(),
    ["Gateway:Dashboard:Retry:DefaultMaxRetries"] = "2",
    ["Gateway:Dashboard:Retry:BackoffBaseMs"] = "1",
    ["Gateway:Dashboard:Retry:BackoffJitterMs"] = "1",
    ["Gateway:Dashboard:RateLimit:Enabled"] = rateEnabled.ToString(),
    ["Gateway:Dashboard:RateLimit:Algorithm"] = rateAlgorithm,
    ["Gateway:Dashboard:RateLimit:PermitLimit"] = (mode == "rate-concurrency" ? 8 : 1000).ToString(),
    ["Gateway:Dashboard:RateLimit:Window"] = "1s",
    ["Gateway:Dashboard:RateLimit:PartitionKey"] = "Global",
    ["Gateway:Dashboard:Plugins:circuit-breaker:Enabled"] = circuitEnabled.ToString(),
    ["Gateway:Dashboard:Plugins:request-retry:Enabled"] = retryEnabled.ToString(),
    ["Gateway:Dashboard:Plugins:rate-limit:Enabled"] = rateEnabled.ToString(),
    ["Gateway:Dashboard:Plugins:waf:Enabled"] = wafEnabled.ToString(),
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
    if (retryEnabled)
    {
        await configService.UpdateRouteMetadataAsync("perf", new Dictionary<string, string>
        {
            ["Retry:Enabled"] = "true",
            ["Retry:MaxRetries"] = "2",
            ["Retry:BackoffBaseMs"] = "1",
            ["Retry:BackoffJitterMs"] = "1",
            ["Retry:RetryOnStatusCodes"] = "502,503,504"
        });
    }
    if (circuitEnabled)
    {
        await configService.UpdateClusterCircuitBreakerAsync("backend", new CircuitBreakerConfig
        {
            Enabled = true,
            FailureThreshold = 3,
            RecoveryTimeoutSeconds = 2,
            HalfOpenMaxAttempts = 1
        });
    }
}
await app.WaitForShutdownAsync();
