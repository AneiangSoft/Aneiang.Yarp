using System.Collections.Concurrent;
using Aneiang.Yarp.Plugins;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Plugin;

/// <summary>
/// Monitors and aggregates per-plugin resource usage statistics.
/// Collects data from IPluginResourceLifecycleCoordinator and per-plugin request counters.
/// </summary>
public sealed class PluginResourceMonitor : IPluginResourceMonitor
{
    private readonly IGatewayPluginManager _pluginManager;
    private readonly IPluginResourceLifecycleCoordinator _lifecycleCoordinator;
    private readonly ILogger<PluginResourceMonitor> _logger;

    // Per-plugin request tracking: (totalRequests, totalErrors, totalLatencyMs)
    private readonly ConcurrentDictionary<string, RequestStats> _requestStats = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _pluginStartTimes = new();
    private readonly ConcurrentDictionary<string, long> _pluginMemoryCounters = new();

    public PluginResourceMonitor(
        IGatewayPluginManager pluginManager,
        IPluginResourceLifecycleCoordinator lifecycleCoordinator,
        ILogger<PluginResourceMonitor> logger)
    {
        _pluginManager = pluginManager;
        _lifecycleCoordinator = lifecycleCoordinator;
        _logger = logger;
    }

    public IReadOnlyList<PluginResourceUsage> GetAllUsage()
    {
        var states = _pluginManager.GetPluginStates();
        var result = new List<PluginResourceUsage>(states.Count);

        foreach (var state in states)
        {
            var usage = BuildUsage(state);
            if (usage != null)
                result.Add(usage);
        }

        return result;
    }

    public PluginResourceUsage? GetUsage(string pluginId)
    {
        var states = _pluginManager.GetPluginStates();
        var state = states.FirstOrDefault(s => s.Manifest.Id == pluginId);
        if (state == null) return null;
        return BuildUsage(state);
    }

    public void RecordRequest(string pluginId, long elapsedMs, bool succeeded)
    {
        var stats = _requestStats.GetOrAdd(pluginId, _ => new RequestStats());
        Interlocked.Increment(ref stats.RequestCount);
        if (!succeeded)
            Interlocked.Increment(ref stats.ErrorCount);
        Interlocked.Add(ref stats.TotalLatencyMs, elapsedMs);

        _pluginStartTimes.TryAdd(pluginId, DateTimeOffset.UtcNow);
    }

    public PluginResourceUsageTotals GetTotals()
    {
        var allUsage = GetAllUsage();
        var enabledCount = allUsage.Count(u => u.Enabled);
        var totalMemory = allUsage.Sum(u => u.MemoryBytes);
        var totalRequests = allUsage.Sum(u => u.RequestCount);
        var totalErrors = allUsage.Sum(u => u.ErrorCount);
        var totalActive = allUsage.Sum(u => u.ActiveResources);
        var avgLatency = totalRequests > 0
            ? allUsage.Sum(u => u.AverageLatencyMs * u.RequestCount) / totalRequests
            : 0;

        return new PluginResourceUsageTotals(
            allUsage.Count,
            enabledCount,
            totalMemory,
            totalRequests,
            totalErrors,
            avgLatency,
            totalActive);
    }

    private PluginResourceUsage? BuildUsage(PluginRuntimeState state)
    {
        var pluginId = state.Manifest.Id;
        var resources = _lifecycleCoordinator.GetRuntimeResources(pluginId);
        var activeResources = resources.Count(r => r.Running);
        var totalResources = resources.Count;

        // Determine overall health from resources
        var overallHealth = PluginResourceHealthStatus.Stopped;
        if (state.Enabled)
        {
            if (resources.Any(r => r.Health == PluginResourceHealthStatus.Faulted))
                overallHealth = PluginResourceHealthStatus.Faulted;
            else if (resources.Any(r => r.Health == PluginResourceHealthStatus.Degraded))
                overallHealth = PluginResourceHealthStatus.Degraded;
            else if (resources.Any(r => r.Health == PluginResourceHealthStatus.Healthy))
                overallHealth = PluginResourceHealthStatus.Healthy;
            else
                overallHealth = PluginResourceHealthStatus.Healthy; // No resources but enabled
        }

        // Get request stats
        var requestCount = 0L;
        var errorCount = 0L;
        var avgLatencyMs = 0.0;
        if (_requestStats.TryGetValue(pluginId, out var stats))
        {
            requestCount = stats.RequestCount;
            errorCount = stats.ErrorCount;
            avgLatencyMs = requestCount > 0 ? (double)stats.TotalLatencyMs / requestCount : 0;
        }

        // Estimate memory: use custom counter or aggregate from resource statistics
        var memoryBytes = _pluginMemoryCounters.GetValueOrDefault(pluginId, 0);
        foreach (var res in resources)
        {
            if (res.Statistics.TryGetValue("memoryBytes", out var mem))
                memoryBytes += mem;
        }

        // Calculate uptime from plugin enable time; fall back to first observed request time.
        var uptime = TimeSpan.Zero;
        var startTime = state.EnabledAt ?? (_pluginStartTimes.TryGetValue(pluginId, out var requestStart) ? requestStart : (DateTimeOffset?)null);
        if (startTime.HasValue)
            uptime = DateTimeOffset.UtcNow - startTime.Value;

        // Merge custom statistics from all resources
        var customStats = new Dictionary<string, long>();
        foreach (var res in resources)
        {
            foreach (var kv in res.Statistics)
            {
                if (kv.Key != "memoryBytes")
                {
                    customStats.TryGetValue(kv.Key, out var existing);
                    customStats[kv.Key] = existing + kv.Value;
                }
            }
        }

        return new PluginResourceUsage(
            pluginId,
            state.Manifest.Name,
            state.Enabled,
            state.IsBuiltIn,
            memoryBytes,
            requestCount,
            errorCount,
            avgLatencyMs,
            activeResources,
            totalResources,
            overallHealth,
            uptime,
            DateTimeOffset.UtcNow,
            customStats);
    }

    /// <summary>Set a custom memory counter for a plugin (e.g., from GC tracking).</summary>
    public void SetMemoryCounter(string pluginId, long bytes)
    {
        _pluginMemoryCounters[pluginId] = bytes;
    }

    private class RequestStats
    {
        public long RequestCount;
        public long ErrorCount;
        public long TotalLatencyMs;
    }
}
