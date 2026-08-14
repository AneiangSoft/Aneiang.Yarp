using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Services;

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
        var redisRateLimit = new Dictionary<string, RedisRateLimitExecutionConfig>(StringComparer.OrdinalIgnoreCase);
        var proxyLog = new Dictionary<string, ProxyLogBindingExecutionConfig>(StringComparer.OrdinalIgnoreCase);
        var circuitBreaker = new Dictionary<string, CircuitBreakerConfig>(StringComparer.OrdinalIgnoreCase);
        var responseCache = new Dictionary<string, ResponseCacheExecutionConfig>(StringComparer.OrdinalIgnoreCase);
        var compression = new Dictionary<string, CompressionExecutionConfig>(StringComparer.OrdinalIgnoreCase);
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
            CompileBinding(bindings, "rate-limit-redis", route.RouteId, WebJson, redisRateLimit);
            CompileBinding(bindings, "proxy-log", route.RouteId, WebJson, proxyLog);
            CompileBinding(bindings, "response-cache", route.RouteId, WebJson, responseCache);
            CompileBinding(bindings, "compression", route.RouteId, WebJson, compression);
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
            redisRateLimit.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            circuitBreaker.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            proxyLog.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            responseCache.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            trafficMetrics.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            clusterMetrics.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            serviceDiscovery.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            compression.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
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
    FrozenDictionary<string, RedisRateLimitExecutionConfig> RedisRateLimitByRoute,
    FrozenDictionary<string, CircuitBreakerConfig> CircuitBreakerByCluster,
    FrozenDictionary<string, ProxyLogBindingExecutionConfig> ProxyLogByRoute,
    FrozenDictionary<string, ResponseCacheExecutionConfig> ResponseCacheByRoute,
    FrozenDictionary<string, MetricsExecutionConfig> TrafficMetricsByRoute,
    FrozenDictionary<string, MetricsExecutionConfig> ClusterMetricsByCluster,
    FrozenDictionary<string, ServiceDiscoveryExecutionConfig> ServiceDiscoveryByCluster,
    FrozenDictionary<string, CompressionExecutionConfig> CompressionByRoute)
{
    public static GatewayPluginExecutionPlan Empty { get; } = new(
        0,
        FrozenDictionary<string, WafBindingOptions>.Empty,
        FrozenDictionary<string, RequestRetryBindingOptions>.Empty,
        FrozenDictionary<string, RateLimitExecutionConfig>.Empty,
        FrozenDictionary<string, RedisRateLimitExecutionConfig>.Empty,
        FrozenDictionary<string, CircuitBreakerConfig>.Empty,
        FrozenDictionary<string, ProxyLogBindingExecutionConfig>.Empty,
        FrozenDictionary<string, ResponseCacheExecutionConfig>.Empty,
        FrozenDictionary<string, MetricsExecutionConfig>.Empty,
        FrozenDictionary<string, MetricsExecutionConfig>.Empty,
        FrozenDictionary<string, ServiceDiscoveryExecutionConfig>.Empty,
        FrozenDictionary<string, CompressionExecutionConfig>.Empty);
}
