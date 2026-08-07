using Aneiang.Yarp.Infrastructure.State;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Plugins.CircuitBreaker.Services;

/// <summary>
/// Warm-up service that pre-populates circuit breaker states from the execution plan.
/// Registered as IHostedService and IPluginRuntimeResource for lifecycle management.
/// </summary>
public sealed class CircuitBreakerWarmupService(
    GatewayPluginExecutionPlanProvider planProvider,
    ICircuitStateStore stateStore,
    ILogger<CircuitBreakerWarmupService> logger) : IHostedService, IPluginRuntimeResource
{
    private int _running;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _stoppedAt;

    public string PluginId => "circuit-breaker";
    public string ResourceId => "circuit-breaker-warmup";
    public string ResourceType => "state-warmup";

    public Task StartAsync(CancellationToken cancellationToken)
        => StartResourceAsync(cancellationToken).AsTask();

    public Task StopAsync(CancellationToken cancellationToken)
        => StopResourceAsync(cancellationToken).AsTask();

    public async ValueTask StartResourceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var configs = planProvider.Current.CircuitBreakerByCluster;
            foreach (var (clusterId, config) in configs)
            {
                stateStore.EnsureCircuitExists(clusterId, config, clusterUid: null);
            }
            _startedAt = DateTimeOffset.UtcNow;
            _stoppedAt = null;
            Interlocked.Exchange(ref _running, 1);
            logger.LogInformation("Circuit breaker warm-up completed for {Count} clusters", configs.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to warm up circuit breaker states");
        }
        await ValueTask.CompletedTask;
    }

    public ValueTask StopResourceAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _running, 0) == 1)
        {
            _stoppedAt = DateTimeOffset.UtcNow;
            logger.LogInformation("Circuit breaker warm-up service stopped");
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<PluginRuntimeResourceSnapshot> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var running = Volatile.Read(ref _running) == 1;
        var snapshot = new PluginRuntimeResourceSnapshot(
            ResourceId,
            ResourceType,
            Running: running,
            Health: running ? PluginResourceHealthStatus.Healthy : PluginResourceHealthStatus.Stopped,
            StartedAt: _startedAt,
            StoppedAt: _stoppedAt,
            Message: null,
            Statistics: new Dictionary<string, long>
            {
                ["clusterCount"] = planProvider.Current.CircuitBreakerByCluster.Count,
                ["memoryBytes"] = stateStore.Count * 1024L
            });
        return ValueTask.FromResult(snapshot);
    }
}
