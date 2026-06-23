using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;
using Aneiang.Yarp.Dashboard.Modules.Notification.Services;
using Aneiang.Yarp.Dashboard.Modules.RateLimit.Middleware;
using Aneiang.Yarp.Dashboard.Modules.Waf.Middleware;
using Aneiang.Yarp.Dashboard.Modules.Waf.Models;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class GatewayMiddlewareBenchmarks
{
    private static readonly RequestDelegate Next = context =>
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        return Task.CompletedTask;
    };

    private WafMiddleware _waf = null!;
    private RateLimitMiddleware _rateLimit = null!;
    private CircuitBreakerMiddleware _circuitBreaker = null!;

    [GlobalSetup]
    public void Setup()
    {
        var dashboardOptions = Options.Create(new DashboardOptions { RoutePrefix = "apigateway" });
        var plugins = new BenchmarkPluginManager("waf", "rate-limit", "circuit-breaker");

        _waf = new WafMiddleware(
            Next,
            NullLogger<WafMiddleware>.Instance,
            Options.Create(new WafOptions
            {
                Enabled = true,
                EnableIpCheck = false,
                EnableRequestSizeValidation = true,
                EnableSqlInjectionDetection = true,
                EnableXssDetection = true,
                EnablePathTraversalDetection = true
            }),
            dashboardOptions,
            new WafEventStore(),
            plugins,
            notificationService: NullNotificationService.Instance);

        _rateLimit = new RateLimitMiddleware(
            Next,
            NullLogger<RateLimitMiddleware>.Instance,
            Options.Create(new RateLimitOptions
            {
                Enabled = true,
                Algorithm = RateLimitAlgorithm.FixedWindow,
                PermitLimit = 1_000_000,
                Window = "1m",
                QueueLimit = 0,
                PartitionKey = "Global"
            }),
            dashboardOptions,
            plugins,
            notificationService: NullNotificationService.Instance);

        _circuitBreaker = new CircuitBreakerMiddleware(
            Next,
            NullLogger<CircuitBreakerMiddleware>.Instance,
            Options.Create(new CircuitBreakerOptions { Enabled = true, MaxCircuitCount = 1000 }),
            dashboardOptions,
            plugins,
            new BenchmarkDynamicYarpConfigService(),
            notificationService: NullNotificationService.Instance);
    }

    [Benchmark(Baseline = true)]
    public Task Baseline_NextOnly()
    {
        return Next(CreateContext("/api/orders", "id=1"));
    }

    [Benchmark]
    public Task Waf_CleanRequest()
    {
        return _waf.InvokeAsync(CreateContext("/api/orders", "id=1&keyword=safe"));
    }

    [Benchmark]
    public Task RateLimit_AllowedRequest()
    {
        return _rateLimit.InvokeAsync(CreateContext("/api/orders", "id=1"));
    }

    [Benchmark]
    public Task CircuitBreaker_NoProxyFeatureFastPath()
    {
        return _circuitBreaker.InvokeAsync(CreateContext("/api/orders", "id=1"));
    }

    private static DefaultHttpContext CreateContext(string path, string queryString)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString("?" + queryString);
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        context.Response.Body = Stream.Null;
        return context;
    }

    private sealed class BenchmarkPluginManager : IGatewayPluginManager
    {
        private readonly HashSet<string> _enabled;

        public BenchmarkPluginManager(params string[] enabled)
        {
            _enabled = new HashSet<string>(enabled, StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<IGatewayPlugin> GetAllPlugins() => Array.Empty<IGatewayPlugin>();
        public IGatewayPlugin? GetPlugin(string pluginId) => null;
        public bool IsPluginEnabled(string pluginId) => _enabled.Contains(pluginId);
        public void SetPluginEnabled(string pluginId, bool enabled)
        {
            if (enabled) _enabled.Add(pluginId);
            else _enabled.Remove(pluginId);
        }
        public void SaveState() { }
    }

    private sealed class BenchmarkDynamicYarpConfigService : IDynamicYarpConfigService
    {
        public Task<RouteOperationResult> TryAddRoute(RegisterRouteRequest request, string source = "dynamic", string? createdBy = null) => Task.FromResult(new RouteOperationResult(true, "ok"));
        public Task<RouteOperationResult> TryRemoveRoute(string routeName, string? clientIp = null, bool removeOrphanedCluster = true) => Task.FromResult(new RouteOperationResult(true, "ok"));
        public Task<RouteOperationResult> TryAddCluster(string clusterId, Dictionary<string, string> destinations, string? loadBalancingPolicy = null, Aneiang.Yarp.Models.HealthCheckConfig? healthCheck = null, string source = "dynamic", string? createdBy = null) => Task.FromResult(new RouteOperationResult(true, "ok"));
        public Task<RouteOperationResult> TryAddCluster(CreateClusterRequest request, string source = "dynamic", string? createdBy = null) => Task.FromResult(new RouteOperationResult(true, "ok"));
        public Task<RouteOperationResult> TryUpdateCluster(string clusterId, UpdateClusterRequest request) => Task.FromResult(new RouteOperationResult(true, "ok"));
        public Task<RouteOperationResult> TryRemoveCluster(string clusterId) => Task.FromResult(new RouteOperationResult(true, "ok"));
        public Task<RouteOperationResult> TryRenameCluster(string oldClusterId, string newClusterId, Dictionary<string, string> destinations, string? loadBalancingPolicy = null, Aneiang.Yarp.Models.HealthCheckConfig? healthCheck = null, string source = "dashboard", string? createdBy = "dashboard-user") => Task.FromResult(new RouteOperationResult(true, "ok"));
        public Task<bool> UpdateRouteMetadataAsync(string routeId, Dictionary<string, string> metadata) => Task.FromResult(true);
        public IReadOnlyList<RouteConfig> GetRoutes() => Array.Empty<RouteConfig>();
        public IReadOnlyList<ClusterConfig> GetClusters() => Array.Empty<ClusterConfig>();
        public ClusterConfig? GetCluster(string clusterId) => null;
        public GatewayDynamicConfig? GetDynamicConfig() => new();
        public void RefreshConfig() { }
        public Task SaveDynamicConfig() => Task.CompletedTask;
        public Task ReplaceAllConfig(IReadOnlyList<RouteConfig> newRoutes, IReadOnlyList<ClusterConfig> newClusters, string source = "rollback", string? createdBy = "dashboard-user") => Task.CompletedTask;
        public bool UpdateHeartbeat(string routeName, string? clientIp = null) => true;
        public Task<RouteOperationResult> TryRenameRoute(string oldRouteId, string newRouteId, RegisterRouteRequest request, string source = "dashboard", string? createdBy = "dashboard-user") => Task.FromResult(new RouteOperationResult(true, "ok"));
        public Task<bool> UpdateClusterCircuitBreakerAsync(string clusterId, CircuitBreakerConfig? config) => Task.FromResult(true);
    }
}
