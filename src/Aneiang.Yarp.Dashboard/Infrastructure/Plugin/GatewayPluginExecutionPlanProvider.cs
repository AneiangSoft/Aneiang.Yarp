using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Dashboard.Modules.Plugins.Runtime;
using Aneiang.Yarp.Dashboard.Modules.Retry;
using Aneiang.Yarp.Dashboard.Modules.Waf;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Plugin;

public sealed class GatewayPluginExecutionPlanProvider
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions EnumJson = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IGatewaySnapshotPublisher _publisher;
    private readonly ILogger<GatewayPluginExecutionPlanProvider> _logger;
    private readonly object _sync = new();
    private GatewaySnapshot? _snapshot;
    private GatewayPluginExecutionPlan _plan = GatewayPluginExecutionPlan.Empty;

    public GatewayPluginExecutionPlanProvider(
        IGatewaySnapshotPublisher publisher,
        ILogger<GatewayPluginExecutionPlanProvider> logger)
    {
        _publisher = publisher;
        _logger = logger;
    }

    public GatewayPluginExecutionPlan Current
    {
        get
        {
            var snapshot = _publisher.Current;
            if (ReferenceEquals(snapshot, Volatile.Read(ref _snapshot)))
                return Volatile.Read(ref _plan);

            lock (_sync)
            {
                if (!ReferenceEquals(snapshot, _snapshot))
                {
                    var compiled = Compile(snapshot);
                    Volatile.Write(ref _plan, compiled);
                    Volatile.Write(ref _snapshot, snapshot);
                }

                return _plan;
            }
        }
    }

    private GatewayPluginExecutionPlan Compile(GatewaySnapshot snapshot)
    {
        var waf = new Dictionary<string, WafBindingOptions>(StringComparer.OrdinalIgnoreCase);
        var retry = new Dictionary<string, RequestRetryBindingOptions>(StringComparer.OrdinalIgnoreCase);
        var rateLimit = new Dictionary<string, RateLimitExecutionConfig>(StringComparer.OrdinalIgnoreCase);
        var proxyLog = new Dictionary<string, ProxyLogBindingExecutionConfig>(StringComparer.OrdinalIgnoreCase);
        var circuitBreaker = new Dictionary<string, CircuitBreakerConfig>(StringComparer.OrdinalIgnoreCase);
        var responseCache = new Dictionary<string, ResponseCacheExecutionConfig>(StringComparer.OrdinalIgnoreCase);
        var distributedRateLimit = new Dictionary<string, DistributedRateLimitExecutionConfig>(StringComparer.OrdinalIgnoreCase);
        var trafficMetrics = new Dictionary<string, MetricsExecutionConfig>(StringComparer.OrdinalIgnoreCase);
        var clusterMetrics = new Dictionary<string, MetricsExecutionConfig>(StringComparer.OrdinalIgnoreCase);
        var serviceDiscovery = new Dictionary<string, ServiceDiscoveryExecutionConfig>(StringComparer.OrdinalIgnoreCase);

        foreach (var route in snapshot.Routes)
        {
            if (!snapshot.RoutePlugins.TryGetValue(route.RouteId, out var bindings)) continue;
            CompileBinding(bindings, "waf", route.RouteId, WebJson, waf);
            CompileBinding(bindings, "request-retry", route.RouteId, WebJson, retry);
            CompileBinding(bindings, "rate-limit", route.RouteId, EnumJson, rateLimit,
                config => config with { RouteUid = StableUid.FromKey("route", route.RouteId) });
            CompileBinding(bindings, "proxy-log", route.RouteId, WebJson, proxyLog);
            CompileBinding(bindings, "response-cache", route.RouteId, WebJson, responseCache);
            CompileBinding(bindings, "distributed-rate-limit", route.RouteId, WebJson, distributedRateLimit);
            CompileBinding(bindings, "traffic-metrics", route.RouteId, WebJson, trafficMetrics);
        }

        foreach (var cluster in snapshot.Clusters)
        {
            if (!snapshot.ClusterPlugins.TryGetValue(cluster.ClusterId, out var bindings)) continue;
            CompileBinding(bindings, "circuit-breaker", cluster.ClusterId, EnumJson, circuitBreaker);
            CompileBinding(bindings, "cluster-metrics", cluster.ClusterId, WebJson, clusterMetrics);
            CompileBinding(bindings, "service-discovery", cluster.ClusterId, WebJson, serviceDiscovery);
        }

        return new GatewayPluginExecutionPlan(
            snapshot.Version,
            waf.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            retry.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            rateLimit.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            circuitBreaker.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            proxyLog.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            responseCache.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            distributedRateLimit.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            trafficMetrics.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            clusterMetrics.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            serviceDiscovery.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
    }

    private void CompileBinding<T>(
        IReadOnlyList<PluginBindingSnapshot> bindings,
        string pluginId,
        string key,
        JsonSerializerOptions options,
        IDictionary<string, T> destination,
        Func<T, T>? transform = null) where T : class
    {
        var binding = bindings.FirstOrDefault(candidate =>
            string.Equals(candidate.PluginId, pluginId, StringComparison.OrdinalIgnoreCase));
        if (binding == null || string.IsNullOrWhiteSpace(binding.ConfigJson)) return;

        try
        {
            var config = JsonSerializer.Deserialize<T>(binding.ConfigJson, options);
            if (config != null) destination[key] = transform == null ? config : transform(config);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Ignoring invalid {PluginId} binding configuration for {Key}", pluginId, key);
        }
    }
}

public sealed record GatewayPluginExecutionPlan(
    long SnapshotVersion,
    FrozenDictionary<string, WafBindingOptions> WafByRoute,
    FrozenDictionary<string, RequestRetryBindingOptions> RetryByRoute,
    FrozenDictionary<string, RateLimitExecutionConfig> RateLimitByRoute,
    FrozenDictionary<string, CircuitBreakerConfig> CircuitBreakerByCluster,
    FrozenDictionary<string, ProxyLogBindingExecutionConfig> ProxyLogByRoute,
    FrozenDictionary<string, ResponseCacheExecutionConfig> ResponseCacheByRoute,
    FrozenDictionary<string, DistributedRateLimitExecutionConfig> DistributedRateLimitByRoute,
    FrozenDictionary<string, MetricsExecutionConfig> TrafficMetricsByRoute,
    FrozenDictionary<string, MetricsExecutionConfig> ClusterMetricsByCluster,
    FrozenDictionary<string, ServiceDiscoveryExecutionConfig> ServiceDiscoveryByCluster)
{
    public static GatewayPluginExecutionPlan Empty { get; } = new(
        0,
        FrozenDictionary<string, WafBindingOptions>.Empty,
        FrozenDictionary<string, RequestRetryBindingOptions>.Empty,
        FrozenDictionary<string, RateLimitExecutionConfig>.Empty,
        FrozenDictionary<string, CircuitBreakerConfig>.Empty,
        FrozenDictionary<string, ProxyLogBindingExecutionConfig>.Empty,
        FrozenDictionary<string, ResponseCacheExecutionConfig>.Empty,
        FrozenDictionary<string, DistributedRateLimitExecutionConfig>.Empty,
        FrozenDictionary<string, MetricsExecutionConfig>.Empty,
        FrozenDictionary<string, MetricsExecutionConfig>.Empty,
        FrozenDictionary<string, ServiceDiscoveryExecutionConfig>.Empty);
}

public sealed record RateLimitExecutionConfig
{
    public bool Enabled { get; init; }
    public RateLimitAlgorithm Algorithm { get; init; } = RateLimitAlgorithm.FixedWindow;
    public int PermitLimit { get; init; } = 100;
    public string Window { get; init; } = "1m";
    public int QueueLimit { get; init; }
    public string PartitionKey { get; init; } = "IpAddress";
    public int SegmentsPerWindow { get; init; } = 4;
    public int TokenLimit { get; init; } = 100;
    public int TokensPerPeriod { get; init; } = 100;
    public string ReplenishmentPeriod { get; init; } = "1s";
    public string RouteUid { get; init; } = string.Empty;
}

public sealed record ProxyLogBindingExecutionConfig
{
    public bool? CaptureRequestHeaders { get; init; }
    public bool? CaptureResponseHeaders { get; init; }
    public bool? RequestBodyCaptureEnabled { get; init; }
    public bool? EnableRequestBodyCapture { get; init; }
    public bool? EnableProxyRequestBodyCapture { get; init; }
    public bool? ResponseBodyCaptureEnabled { get; init; }
    public bool? EnableResponseBodyCapture { get; init; }
    public bool? EnableProxyResponseBodyCapture { get; init; }
    public int? MaxBodyLength { get; init; }
    public int? LogMaxBodyLength { get; init; }
    public int? MaxBodyBufferBytes { get; init; }
    public int? LogMaxBodyBufferBytes { get; init; }
    public bool? ErrorsOnly { get; init; }
    public bool? LogErrorsOnly { get; init; }
    public bool? SamplingEnabled { get; init; }
    public bool? EnableSampling { get; init; }
    public bool? EnableLogSampling { get; init; }
    public double? SamplingRate { get; init; }
    public double? LogSamplingRate { get; init; }
}
