using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aneiang.Yarp.Infrastructure.Middleware;
using Aneiang.Yarp.Plugins;
using Aneiang.Yarp.Infrastructure.State;
using Aneiang.Yarp.Infrastructure.Resilience;
using Yarp.ReverseProxy.Model;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Models;

namespace Aneiang.Yarp.Plugin.CircuitBreaker;

/// <summary>
/// Per-destination circuit breaker middleware.
/// Reads configuration from cluster-level CircuitBreakerConfig.
/// Tracks consecutive failures and opens the circuit when threshold is reached.
/// States: Closed → Open → HalfOpen → Closed.
/// 
/// Circuit state is managed via <see cref="ICircuitStateStore"/> (Singleton),
/// shared with controllers, warmup services, and retry middleware.
/// </summary>
public sealed class CircuitBreakerMiddleware : GatewayMiddlewareBase
{
    private const int MaxCircuitCount = 10_000;

    private readonly ILogger<CircuitBreakerMiddleware> _logger;
    private readonly GatewayPluginExecutionPlanProvider _executionPlans;
    private readonly ICircuitStateStore _circuitStore;
    private readonly IDestinationCandidateCoordinator _destinationCoordinator;

    private static readonly TimeSpan _cleanupThreshold = TimeSpan.FromHours(3);
    private long _lastCleanupTicks = DateTime.Now.Ticks;

    public CircuitBreakerMiddleware(
        RequestDelegate next,
        ILogger<CircuitBreakerMiddleware> logger,
        IOptions<GatewayMiddlewareOptions> dashOptions,
        IGatewayPluginManager pluginManager,
        IDynamicYarpConfigService yarpConfig,
        ICircuitStateStore circuitStore,
        GatewayPluginExecutionPlanProvider executionPlans,
        IDestinationCandidateCoordinator destinationCoordinator)
        : base(next, dashOptions, pluginManager, yarpConfig)
    {
        _logger = logger;
        _executionPlans = executionPlans;
        _circuitStore = circuitStore;
        _destinationCoordinator = destinationCoordinator;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsDashboardRequest(context))
        {
            await Next(context);
            return;
        }

        var proxyFeature = context.Features.Get<IReverseProxyFeature>();
        var clusterId = proxyFeature?.Cluster?.Config?.ClusterId;

        if (string.IsNullOrWhiteSpace(clusterId) ||
            !TryGetBindingConfig(clusterId, out var clusterUid, out var cbConfig))
        {
            await Next(context);
            return;
        }

        var candidates = await _destinationCoordinator.ApplyAsync(context, excludeAttempted: false, context.RequestAborted);
        if (candidates.Count == 0)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "ServiceUnavailable",
                message = $"No healthy destination with a closed circuit is available for cluster '{clusterId}'."
            });
            return;
        }

        var destinationId = proxyFeature?.ProxiedDestination?.DestinationId;
        var circuitKey = CircuitKeyHelper.BuildCircuitKey(clusterUid, clusterId, destinationId);

        if (_circuitStore.Count >= MaxCircuitCount && !_circuitStore.ContainsKey(circuitKey))
        {
            _logger.LogWarning("Circuit count limit reached ({Max}), skipping new circuit for {CircuitKey}",
                MaxCircuitCount, circuitKey);
            await Next(context);
            return;
        }

        var state = _circuitStore.GetOrAdd(circuitKey, _ => new CircuitState(cbConfig));
        state.ApplyConfig(cbConfig);
        state.ClusterUid = clusterUid ?? StableUid.FromKey("cluster", clusterId);
        state.ClusterKeySnapshot = clusterId;
        state.DestinationUid = CircuitKeyHelper.ResolveDestinationUid(destinationId);
        state.DestinationKeySnapshot = destinationId ?? "any";
        // F3 fix: LastAccessedAt is now only written inside the lock in UpdateCircuitState (finally block).
        // The previous unprotected write here raced with the locked write.

        TryCleanupStaleCircuits();

        // All state transitions are protected by a per-circuit lock to prevent race conditions.
        // We determine the action to take inside the lock, then execute it outside the lock.
        CircuitAction action;
        lock (state.SyncRoot)
        {
            if (state.Status == CircuitStatus.Open)
            {
                if (DateTime.Now < state.OpenedAt + state.RecoveryTimeout)
                {
                    _logger.LogWarning(
                        "Circuit OPEN for {CircuitKey} (recovery at {RecoveryAt})",
                        circuitKey, state.OpenedAt + state.RecoveryTimeout);
                    action = CircuitAction.RejectOpen;
                }
                else
                {
                    state.Status = CircuitStatus.HalfOpen;
                    state.HalfOpenRequests = 0;
                    _logger.LogInformation("Circuit HALF-OPEN for {CircuitKey}", circuitKey);
                    action = CircuitAction.Proceed;
                }
            }
            else if (state.Status == CircuitStatus.HalfOpen)
            {
                if (state.HalfOpenRequests >= state.MaxHalfOpenAttempts)
                {
                    action = CircuitAction.RejectHalfOpen;
                }
                else
                {
                    state.HalfOpenRequests++;
                    action = CircuitAction.Proceed;
                }
            }
            else
            {
                action = CircuitAction.Proceed;
            }
        }

        if (action == CircuitAction.RejectOpen)
        {
            context.Response.StatusCode = 503;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "ServiceUnavailable",
                message = $"Circuit breaker open for cluster '{clusterId}'. Retry later."
            });
            return;
        }

        if (action == CircuitAction.RejectHalfOpen)
        {
            context.Response.StatusCode = 503;
            context.Response.ContentType = "application/json";
            context.Response.Headers["Retry-After"] = "5";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "ServiceUnavailable",
                message = $"Circuit breaker half-open for cluster '{clusterId}'."
            });
            return;
        }

        try
        {
            await Next(context);
        }
        finally
        {
            var actualDestination = proxyFeature?.ProxiedDestination;
            if (actualDestination is not null)
            {
                _destinationCoordinator.MarkAttempted(context, actualDestination);
                var failed = cbConfig.FailureStatusCodes.Contains(context.Response.StatusCode) || context.Response.StatusCode >= 500;
                _circuitStore.RecordOutcome(clusterId, actualDestination.DestinationId, failed, clusterUid, cbConfig);
            }
            else
            {
                UpdateCircuitState(state, circuitKey, context.Response.StatusCode, cbConfig);
            }
        }
    }

    private bool TryGetBindingConfig(
        string clusterId,
        out string? clusterUid,
        out CircuitBreakerConfig config)
    {
        clusterUid = null;
        config = null!;

        if (!_executionPlans.Current.CircuitBreakerByCluster.TryGetValue(clusterId, out var resolved) || !resolved.Enabled)
            return false;

        config = resolved;
        return true;
    }

    private void UpdateCircuitState(CircuitState state, string circuitKey, int statusCode, CircuitBreakerConfig cbConfig)
    {
        var isFailure = cbConfig.FailureStatusCodes.Contains(statusCode) || statusCode >= 500;

        lock (state.SyncRoot)
        {
            state.LastAccessedAt = DateTime.Now;

            if (isFailure)
            {
                state.ConsecutiveFailures++;

                if (state.Status == CircuitStatus.HalfOpen)
                {
                    state.Status = CircuitStatus.Open;
                    state.OpenedAt = DateTime.Now;
                    _logger.LogWarning("Circuit HALF-OPEN probe FAILED for {CircuitKey}, back to OPEN", circuitKey);
                }
                else if (state.ConsecutiveFailures >= state.FailureThreshold)
                {
                    state.Status = CircuitStatus.Open;
                    state.OpenedAt = DateTime.Now;
                    _logger.LogWarning("Circuit OPENED for {CircuitKey} after {Failures} failures", circuitKey, state.ConsecutiveFailures);
                }
            }
            else
            {
                if (state.Status == CircuitStatus.HalfOpen)
                {
                    state.Status = CircuitStatus.Closed;
                    state.ConsecutiveFailures = 0;
                    _logger.LogInformation("Circuit CLOSED for {CircuitKey}", circuitKey);
                }
                else
                {
                    state.ConsecutiveFailures = 0;
                }
            }
        }
    }

    private void TryCleanupStaleCircuits()
    {
        var now = DateTime.Now;
        var lastTicks = Interlocked.Read(ref _lastCleanupTicks);
        if (now - new DateTime(lastTicks, DateTimeKind.Local) < _cleanupThreshold)
            return;

        Interlocked.Exchange(ref _lastCleanupTicks, now.Ticks);
        _circuitStore.CleanupStale(_cleanupThreshold);
    }
}
