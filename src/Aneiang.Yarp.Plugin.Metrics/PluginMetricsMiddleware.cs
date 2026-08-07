using System.Collections.Concurrent;
using Aneiang.Yarp.Infrastructure.Performance;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using Yarp.ReverseProxy.Model;

namespace Aneiang.Yarp.Plugin.Metrics;

public sealed record PluginMetricSnapshot(long Requests, long Errors, long RequestBytes, long ResponseBytes, long DurationMicroseconds);

public sealed class PluginMetricStore
{
    private readonly ConcurrentDictionary<string, Counters> _route = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Counters> _cluster = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Counters> _destination = new(StringComparer.OrdinalIgnoreCase);

    public void Record(string? routeId, string? clusterId, string? destinationId, int statusCode, long durationMicroseconds, long requestBytes, long responseBytes)
    {
        if (routeId != null) Record(_route.GetOrAdd(routeId, _ => new()), statusCode, durationMicroseconds, requestBytes, responseBytes);
        if (clusterId != null) Record(_cluster.GetOrAdd(clusterId, _ => new()), statusCode, durationMicroseconds, requestBytes, responseBytes);
        if (clusterId != null && destinationId != null) Record(_destination.GetOrAdd($"{clusterId}/{destinationId}", _ => new()), statusCode, durationMicroseconds, requestBytes, responseBytes);
    }

    public IReadOnlyDictionary<string, PluginMetricSnapshot> GetRoutes() => Snapshot(_route);
    public IReadOnlyDictionary<string, PluginMetricSnapshot> GetClusters() => Snapshot(_cluster);
    public IReadOnlyDictionary<string, PluginMetricSnapshot> GetDestinations() => Snapshot(_destination);

    private static void Record(Counters counters, int statusCode, long duration, long requestBytes, long responseBytes)
    {
        Interlocked.Increment(ref counters.Requests);
        if (statusCode >= 500) Interlocked.Increment(ref counters.Errors);
        Interlocked.Add(ref counters.DurationMicroseconds, duration);
        Interlocked.Add(ref counters.RequestBytes, Math.Max(0, requestBytes));
        Interlocked.Add(ref counters.ResponseBytes, Math.Max(0, responseBytes));
    }

    private static IReadOnlyDictionary<string, PluginMetricSnapshot> Snapshot(ConcurrentDictionary<string, Counters> source) =>
        source.ToDictionary(pair => pair.Key, pair => new PluginMetricSnapshot(
            Interlocked.Read(ref pair.Value.Requests), Interlocked.Read(ref pair.Value.Errors),
            Interlocked.Read(ref pair.Value.RequestBytes), Interlocked.Read(ref pair.Value.ResponseBytes),
            Interlocked.Read(ref pair.Value.DurationMicroseconds)), StringComparer.OrdinalIgnoreCase);

    private sealed class Counters { public long Requests; public long Errors; public long RequestBytes; public long ResponseBytes; public long DurationMicroseconds; }
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
