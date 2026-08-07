namespace Aneiang.Yarp.Plugins;

public enum PluginResourceHealthStatus
{
    Stopped,
    Starting,
    Healthy,
    Degraded,
    Faulted,
    Stopping
}

public sealed record PluginRuntimeResourceSnapshot(
    string ResourceId,
    string ResourceType,
    bool Running,
    PluginResourceHealthStatus Health,
    DateTimeOffset? StartedAt,
    DateTimeOffset? StoppedAt,
    string? Message,
    IReadOnlyDictionary<string, long> Statistics);

/// <summary>Controllable runtime resource owned by one plugin. Registration in DI does not start the resource.</summary>
public interface IPluginRuntimeResource
{
    string PluginId { get; }
    string ResourceId { get; }
    string ResourceType { get; }
    ValueTask StartResourceAsync(CancellationToken cancellationToken);
    ValueTask StopResourceAsync(CancellationToken cancellationToken);
    ValueTask<PluginRuntimeResourceSnapshot> CheckHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>Aggregated resource usage for a single plugin.</summary>
public sealed record PluginResourceUsage(
    string PluginId,
    string DisplayName,
    bool Enabled,
    bool IsBuiltIn,
    long MemoryBytes,
    long RequestCount,
    long ErrorCount,
    double AverageLatencyMs,
    int ActiveResources,
    int TotalResources,
    PluginResourceHealthStatus OverallHealth,
    TimeSpan Uptime,
    DateTimeOffset LastUpdated,
    IReadOnlyDictionary<string, long> CustomStatistics);

/// <summary>Monitors and aggregates per-plugin resource usage statistics.</summary>
public interface IPluginResourceMonitor
{
    /// <summary>Get current resource usage for all plugins.</summary>
    IReadOnlyList<PluginResourceUsage> GetAllUsage();

    /// <summary>Get resource usage for a specific plugin.</summary>
    PluginResourceUsage? GetUsage(string pluginId);

    /// <summary>Record a request processed by a plugin.</summary>
    void RecordRequest(string pluginId, long elapsedMs, bool succeeded);

    /// <summary>Get aggregated totals across all plugins.</summary>
    PluginResourceUsageTotals GetTotals();
}

/// <summary>Aggregated totals across all plugins.</summary>
public sealed record PluginResourceUsageTotals(
    int TotalPlugins,
    int EnabledPlugins,
    long TotalMemoryBytes,
    long TotalRequestCount,
    long TotalErrorCount,
    double OverallAverageLatencyMs,
    int TotalActiveResources);


public interface IPluginResourceLifecycleCoordinator
{
    Task ReconcileAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<PluginRuntimeResourceSnapshot> GetRuntimeResources(string pluginId);
}
