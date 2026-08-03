using System.Collections.Concurrent;

namespace Aneiang.Yarp.Dashboard.Modules.Plugins.Runtime;

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
