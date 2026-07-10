using Aneiang.Yarp.Dashboard.Infrastructure.State;
using Aneiang.Yarp.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Services;

/// <summary>
/// Recreates in-memory circuit entries from persisted cluster circuit breaker configuration at startup.
/// Runtime circuit state is intentionally in-memory, but enabled cluster policies should be visible after restart.
/// </summary>
public sealed class CircuitBreakerWarmupService : IHostedService
{
    private readonly IDynamicYarpConfigService _yarpConfig;
    private readonly ICircuitStateStore _circuitStore;
    private readonly ILogger<CircuitBreakerWarmupService> _logger;

    public CircuitBreakerWarmupService(
        IDynamicYarpConfigService yarpConfig,
        ICircuitStateStore circuitStore,
        ILogger<CircuitBreakerWarmupService> logger)
    {
        _yarpConfig = yarpConfig;
        _circuitStore = circuitStore;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var dynConfig = _yarpConfig.GetDynamicConfig();
        if (dynConfig?.Clusters == null) return Task.CompletedTask;

        var count = 0;
        foreach (var cluster in dynConfig.Clusters)
        {
            if (cluster.CircuitBreaker is { Enabled: true } cbConfig)
            {
                _circuitStore.EnsureCircuitExists(cluster.Config.ClusterId ?? string.Empty, cbConfig, cluster.ClusterUid);
                count++;
            }
        }

        if (count > 0)
            _logger.LogInformation("Circuit breaker warmup restored {Count} configured circuit(s)", count);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
