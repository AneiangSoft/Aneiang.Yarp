using System.Collections.Concurrent;
using System.Diagnostics;
using Aneiang.Yarp.Dashboard.Infrastructure.Performance;
using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Yarp.ReverseProxy.Model;

namespace Aneiang.Yarp.Dashboard.Modules.Plugins.Runtime;

public sealed record ResponseCacheExecutionConfig
{
    public bool Enabled { get; init; } = true;
    public int TtlSeconds { get; init; } = 60;
    public int MaxBodyBytes { get; init; } = 1_048_576;
    public bool VaryByQuery { get; init; } = true;
    public string[] VaryHeaders { get; init; } = [];
    public int[] CacheStatusCodes { get; init; } = [200];
}

public sealed record DistributedRateLimitExecutionConfig
{
    public bool Enabled { get; init; } = true;
    public int PermitLimit { get; init; } = 100;
    public int WindowSeconds { get; init; } = 60;
    public string PartitionKey { get; init; } = "IpAddress";
    public string UserHeader { get; init; } = "X-User-Id";
    public string Backend { get; init; } = "Memory";
}

public sealed record MetricsExecutionConfig
{
    public bool Enabled { get; init; } = true;
    public bool IncludeRequestBytes { get; init; } = true;
    public bool IncludeResponseBytes { get; init; } = true;
    public bool IncludeDestination { get; init; } = true;
}

public sealed record ServiceDiscoveryExecutionConfig
{
    public bool Enabled { get; init; } = true;
    public string Mode { get; init; } = "Static";
    public string[] StaticEndpoints { get; init; } = [];
    public string? Endpoint { get; init; }
    public string? ServiceName { get; init; }
    public string Namespace { get; init; } = "default";
    public string Scheme { get; init; } = "http";
    public int RefreshSeconds { get; init; } = 30;
    public int RequestTimeoutSeconds { get; init; } = 5;
}

public sealed class ResponseCacheMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly GatewayPluginExecutionPlanProvider _plans;

    public ResponseCacheMiddleware(RequestDelegate next, IMemoryCache cache, GatewayPluginExecutionPlanProvider plans)
    {
        _next = next;
        _cache = cache;
        _plans = plans;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var routeId = context.Features.Get<IReverseProxyFeature>()?.Route?.Config?.RouteId;
        if (context.Request.Method is not ("GET" or "HEAD") || string.IsNullOrWhiteSpace(routeId) ||
            !_plans.Current.ResponseCacheByRoute.TryGetValue(routeId, out var config) || !config.Enabled)
        {
            await _next(context);
            return;
        }

        var query = config.VaryByQuery ? context.Request.QueryString.Value : null;
        var headerParts = config.VaryHeaders.Select(name => $"{name}={context.Request.Headers[name]}");
        var key = $"plugin-cache:{routeId}:{context.Request.Path}:{query}:{string.Join('|', headerParts)}";
        if (_cache.TryGetValue(key, out CachedResponse? cached) && cached != null)
        {
            context.Response.StatusCode = cached.StatusCode;
            foreach (var header in cached.Headers) context.Response.Headers[header.Key] = header.Value;
            context.Response.Headers.ContentLength = cached.Body.Length;
            if (context.Request.Method != "HEAD") await context.Response.Body.WriteAsync(cached.Body, context.RequestAborted);
            return;
        }

        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context);
            var body = buffer.ToArray();
            buffer.Position = 0;
            await buffer.CopyToAsync(original, context.RequestAborted);
            if (body.Length <= config.MaxBodyBytes && config.CacheStatusCodes.Contains(context.Response.StatusCode) &&
                !context.Response.Headers.ContainsKey("Set-Cookie") && !context.Response.Headers.CacheControl.ToString().Contains("no-store", StringComparison.OrdinalIgnoreCase))
            {
                var headers = context.Response.Headers.ToDictionary(x => x.Key, x => x.Value.ToString(), StringComparer.OrdinalIgnoreCase);
                _cache.Set(key, new CachedResponse(context.Response.StatusCode, headers, body), TimeSpan.FromSeconds(config.TtlSeconds));
            }
        }
        finally
        {
            context.Response.Body = original;
        }
    }

    private sealed record CachedResponse(int StatusCode, IReadOnlyDictionary<string, string> Headers, byte[] Body);
}

public sealed class DistributedRateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly GatewayPluginExecutionPlanProvider _plans;
    private readonly DistributedRateLimitBackendResolver _backends;

    public DistributedRateLimitMiddleware(RequestDelegate next, GatewayPluginExecutionPlanProvider plans, DistributedRateLimitBackendResolver backends)
    {
        _next = next;
        _plans = plans;
        _backends = backends;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var routeId = context.Features.Get<IReverseProxyFeature>()?.Route?.Config?.RouteId;
        if (string.IsNullOrWhiteSpace(routeId) || !_plans.Current.DistributedRateLimitByRoute.TryGetValue(routeId, out var config) || !config.Enabled)
        {
            await _next(context);
            return;
        }

        var partition = config.PartitionKey switch
        {
            "Global" => "global",
            "Route" => routeId,
            "UserId" => context.Request.Headers[config.UserHeader].FirstOrDefault() ?? "anonymous",
            _ => context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
        };
        var windowSeconds = Math.Clamp(config.WindowSeconds, 1, 86_400);
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var bucket = nowSeconds / windowSeconds;
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds((bucket + 1) * windowSeconds);
        var key = $"{routeId}:{partition}:{bucket}";
        var count = await _backends.Resolve(config.Backend).IncrementAsync(key, expiresAt, context.RequestAborted);
        if (count > config.PermitLimit)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = Math.Max(1, expiresAt.ToUnixTimeSeconds() - nowSeconds).ToString();
            await context.Response.WriteAsJsonAsync(new { error = "TooManyRequests", routeId }, context.RequestAborted);
            return;
        }
        await _next(context);
    }
}

public sealed class PluginMetricsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly GatewayPluginExecutionPlanProvider _plans;
    private readonly LockFreeStatistics _statistics;
    private readonly PluginMetricStore _metricStore;

    public PluginMetricsMiddleware(RequestDelegate next, GatewayPluginExecutionPlanProvider plans, LockFreeStatistics statistics, PluginMetricStore metricStore)
    {
        _next = next;
        _plans = plans;
        _statistics = statistics;
        _metricStore = metricStore;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var feature = context.Features.Get<IReverseProxyFeature>();
        var routeId = feature?.Route?.Config?.RouteId;
        var clusterId = feature?.Cluster?.Config?.ClusterId;
        MetricsExecutionConfig? routeConfig = null;
        MetricsExecutionConfig? clusterConfig = null;
        var routeEnabled = routeId != null && _plans.Current.TrafficMetricsByRoute.TryGetValue(routeId, out routeConfig) && routeConfig.Enabled;
        var clusterEnabled = clusterId != null && _plans.Current.ClusterMetricsByCluster.TryGetValue(clusterId, out clusterConfig) && clusterConfig.Enabled;
        if (!routeEnabled && !clusterEnabled)
        {
            await _next(context);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        await _next(context);
        var elapsedMicros = (long)(Stopwatch.GetElapsedTime(started).TotalMilliseconds * 1000d);
        var destinationId = feature?.ProxiedDestination?.DestinationId;
        var requestBytes = routeEnabled && routeConfig!.IncludeRequestBytes ? context.Request.ContentLength ?? 0 : 0;
        var responseBytes = routeEnabled && routeConfig!.IncludeResponseBytes ? context.Response.ContentLength ?? 0 : 0;
        _metricStore.Record(routeEnabled ? routeId : null, clusterEnabled ? clusterId : null,
            clusterEnabled && clusterConfig!.IncludeDestination ? destinationId : null,
            context.Response.StatusCode, elapsedMicros, requestBytes, responseBytes);
        _statistics.RecordRequest(
            context.Response.StatusCode,
            elapsedMicros,
            routeId == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(routeId),
            clusterId == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(clusterId));
    }
}
