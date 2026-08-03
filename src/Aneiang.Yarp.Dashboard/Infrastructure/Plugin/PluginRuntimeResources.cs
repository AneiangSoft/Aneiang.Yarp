using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Plugin;

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

public interface IPluginResourceLifecycleCoordinator
{
    Task ReconcileAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<PluginRuntimeResourceSnapshot> GetRuntimeResources(string pluginId);
}

/// <summary>
/// Reconciles plugin-owned resources against effective activation. This controls resource loops only;
/// it deliberately does not add or remove registrations from the built service provider.
/// </summary>
public sealed class PluginResourceLifecycleCoordinator : BackgroundService, IPluginResourceLifecycleCoordinator
{
    private readonly IGatewayPluginManager _pluginManager;
    private readonly GatewayPluginExecutionPlanProvider _executionPlans;
    private readonly IReadOnlyList<IPluginRuntimeResource> _resources;
    private readonly ILogger<PluginResourceLifecycleCoordinator> _logger;
    private readonly SemaphoreSlim _reconcileLock = new(1, 1);
    private volatile IReadOnlyList<PluginRuntimeResourceSnapshot> _snapshots = Array.Empty<PluginRuntimeResourceSnapshot>();

    public PluginResourceLifecycleCoordinator(
        IGatewayPluginManager pluginManager,
        GatewayPluginExecutionPlanProvider executionPlans,
        IEnumerable<IPluginRuntimeResource> resources,
        ILogger<PluginResourceLifecycleCoordinator> logger)
    {
        _pluginManager = pluginManager;
        _executionPlans = executionPlans;
        _resources = resources.ToArray();
        _logger = logger;
    }

    public IReadOnlyList<PluginRuntimeResourceSnapshot> GetRuntimeResources(string pluginId) =>
        _snapshots.Where(resource => resource.ResourceId.StartsWith(pluginId + ":", StringComparison.OrdinalIgnoreCase)).ToArray();

    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        await _reconcileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var plan = _executionPlans.Current;
            foreach (var resource in _resources)
            {
                try
                {
                    var shouldRun = _pluginManager.IsPluginEnabled(resource.PluginId) && HasEnabledBinding(resource.PluginId, plan);
                    var current = await resource.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
                    if (shouldRun && !current.Running)
                        await resource.StartResourceAsync(cancellationToken).ConfigureAwait(false);
                    else if (!shouldRun && current.Health != PluginResourceHealthStatus.Stopped)
                        await resource.StopResourceAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to reconcile runtime resource {ResourceId}", resource.ResourceId);
                }
            }

            await RefreshSnapshotsAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _reconcileLock.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ReconcileAsync(stoppingToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _reconcileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var resource in _resources.Reverse())
            {
                try
                {
                    await resource.StopResourceAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to stop runtime resource {ResourceId}", resource.ResourceId);
                }
            }
            await RefreshSnapshotsAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _reconcileLock.Release();
        }
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshSnapshotsAsync(CancellationToken cancellationToken)
    {
        var snapshots = new List<PluginRuntimeResourceSnapshot>(_resources.Count);
        foreach (var resource in _resources)
        {
            try
            {
                snapshots.Add(await resource.CheckHealthAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to check runtime resource {ResourceId}", resource.ResourceId);
                snapshots.Add(new PluginRuntimeResourceSnapshot(
                    resource.ResourceId,
                    resource.ResourceType,
                    false,
                    PluginResourceHealthStatus.Faulted,
                    null,
                    DateTimeOffset.UtcNow,
                    exception.Message,
                    new Dictionary<string, long>()));
            }
        }
        _snapshots = snapshots;
    }

    private static bool HasEnabledBinding(string pluginId, GatewayPluginExecutionPlan plan) => pluginId switch
    {
        "proxy-log" => plan.ProxyLogByRoute.Count > 0,
        "circuit-breaker" => plan.CircuitBreakerByCluster.Count > 0,
        "service-discovery" => plan.ServiceDiscoveryByCluster.Values.Any(config => config.Enabled),
        _ => false
    };
}
