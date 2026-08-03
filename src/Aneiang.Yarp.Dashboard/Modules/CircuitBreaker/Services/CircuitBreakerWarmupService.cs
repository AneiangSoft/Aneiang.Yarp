using System.Text.Json;
using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Aneiang.Yarp.Dashboard.Infrastructure.State;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Services;

/// <summary>
/// Recreates in-memory circuit entries from persisted cluster circuit breaker configuration at startup.
/// Runtime circuit state is intentionally in-memory, but enabled cluster policies should be visible after restart.
/// </summary>
public sealed class CircuitBreakerWarmupService : IPluginRuntimeResource
{
    private const string PluginId = "circuit-breaker";

    private readonly GatewayPluginExecutionPlanProvider _executionPlans;
    private readonly IGatewayPluginManager _pluginManager;
    private readonly ICircuitStateStore _circuitStore;
    private readonly ILogger<CircuitBreakerWarmupService> _logger;
    private bool _running;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _stoppedAt;
    private Exception? _lastError;
    private long _warmedCircuits;

    string IPluginRuntimeResource.PluginId => PluginId;
    public string ResourceId => "circuit-breaker:state-warmup";
    public string ResourceType => "warmup";

    public CircuitBreakerWarmupService(
        GatewayPluginExecutionPlanProvider executionPlans,
        IGatewayPluginManager pluginManager,
        ICircuitStateStore circuitStore,
        ILogger<CircuitBreakerWarmupService> logger)
    {
        _executionPlans = executionPlans;
        _pluginManager = pluginManager;
        _circuitStore = circuitStore;
        _logger = logger;
    }

    public ValueTask StartResourceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var count = 0;
            foreach (var (clusterId, cbConfig) in _executionPlans.Current.CircuitBreakerByCluster)
            {
                if (!cbConfig.Enabled) continue;
                _circuitStore.EnsureCircuitExists(clusterId, cbConfig, null);
                count++;
            }
            Interlocked.Exchange(ref _warmedCircuits, count);
            _lastError = null;
            _running = true;
            _startedAt = DateTimeOffset.UtcNow;
            if (count > 0) _logger.LogInformation("Circuit breaker warmup restored {Count} configured circuit(s)", count);
        }
        catch (Exception exception)
        {
            _lastError = exception;
            _running = false;
            throw;
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask StopResourceAsync(CancellationToken cancellationToken)
    {
        _running = false;
        _stoppedAt = DateTimeOffset.UtcNow;
        return ValueTask.CompletedTask;
    }

    public ValueTask<PluginRuntimeResourceSnapshot> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var health = _lastError is not null ? PluginResourceHealthStatus.Faulted :
            _running ? PluginResourceHealthStatus.Healthy : PluginResourceHealthStatus.Stopped;
        return ValueTask.FromResult(new PluginRuntimeResourceSnapshot(ResourceId, ResourceType, _running, health,
            _startedAt, _stoppedAt, _lastError?.Message,
            new Dictionary<string, long> { ["warmedCircuits"] = Interlocked.Read(ref _warmedCircuits) }));
    }
}
