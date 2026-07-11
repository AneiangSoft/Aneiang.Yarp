using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Middleware;
using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Aneiang.Yarp.Dashboard.Infrastructure.State;
using Aneiang.Yarp.Dashboard.Modules.Notification.Services;
using Yarp.ReverseProxy.Model;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Models;

namespace Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;

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
    private readonly ILogger<CircuitBreakerMiddleware> _logger;
    private readonly CircuitBreakerOptions _options;
    private readonly IDynamicYarpConfigService _yarpConfig;
    private readonly INotificationService _notificationService;
    private readonly ICircuitStateStore _circuitStore;

    private static readonly TimeSpan _cleanupThreshold = TimeSpan.FromHours(3);
    private long _lastCleanupTicks = DateTime.UtcNow.Ticks;

    public CircuitBreakerMiddleware(
        RequestDelegate next,
        ILogger<CircuitBreakerMiddleware> logger,
        IOptions<CircuitBreakerOptions> options,
        IOptions<DashboardOptions> dashOptions,
        IGatewayPluginManager pluginManager,
        IDynamicYarpConfigService yarpConfig,
        ICircuitStateStore circuitStore,
        INotificationService? notificationService = null)
        : base(next, dashOptions, pluginManager, yarpConfig)
    {
        _logger = logger;
        _options = options.Value;
        _yarpConfig = yarpConfig;
        _notificationService = notificationService ?? NullNotificationService.Instance;
        _circuitStore = circuitStore;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsDashboardRequest(context))
        {
            await Next(context);
            return;
        }

        if (!IsPluginEnabled("circuit-breaker"))
        {
            await Next(context);
            return;
        }

        var proxyFeature = context.Features.Get<IReverseProxyFeature>();
        var clusterId = proxyFeature?.Route?.Config?.ClusterId;
        var destinationId = proxyFeature?.ProxiedDestination?.DestinationId;

        if (string.IsNullOrEmpty(clusterId))
        {
            await Next(context);
            return;
        }

        var clusterUid = ResolveClusterUid(clusterId);
        var circuitKey = InMemoryCircuitStateStore.BuildCircuitKey(clusterUid, clusterId, destinationId);

        var cbConfig = GetClusterCircuitBreakerConfig(clusterId);
        if (cbConfig == null || !cbConfig.Enabled)
        {
            await Next(context);
            return;
        }

        var effectiveOptions = InMemoryCircuitStateStore.ToOptions(cbConfig, _options.MaxCircuitCount);

        if (_circuitStore.Count >= _options.MaxCircuitCount && !_circuitStore.ContainsKey(circuitKey))
        {
            _logger.LogWarning("Circuit count limit reached ({Max}), skipping new circuit for {CircuitKey}",
                _options.MaxCircuitCount, circuitKey);
            await Next(context);
            return;
        }

        var state = _circuitStore.GetOrAdd(circuitKey, _ => new CircuitState(effectiveOptions));
        state.ClusterUid = clusterUid ?? StableUid.FromKey("cluster", clusterId);
        state.ClusterKeySnapshot = clusterId;
        state.DestinationUid = InMemoryCircuitStateStore.ResolveDestinationUid(destinationId);
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
                if (DateTime.UtcNow < state.OpenedAt + state.RecoveryTimeout)
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
            UpdateCircuitState(state, circuitKey, context.Response.StatusCode, cbConfig);
        }
    }

    private CircuitBreakerConfig? GetClusterCircuitBreakerConfig(string clusterId)
    {
        var dynConfig = _yarpConfig.GetDynamicConfig();
        return dynConfig?.Clusters.FirstOrDefault(c =>
            string.Equals(c.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase))
            ?.CircuitBreaker;
    }



    private void UpdateCircuitState(CircuitState state, string circuitKey, int statusCode, CircuitBreakerConfig cbConfig)
    {
        var isFailure = cbConfig.FailureStatusCodes.Contains(statusCode) || statusCode >= 500;

        lock (state.SyncRoot)
        {
            state.LastAccessedAt = DateTime.UtcNow;

            if (isFailure)
            {
                state.ConsecutiveFailures++;

                if (state.Status == CircuitStatus.HalfOpen)
                {
                    state.Status = CircuitStatus.Open;
                    state.OpenedAt = DateTime.UtcNow;
                    _logger.LogWarning("Circuit HALF-OPEN probe FAILED for {CircuitKey}, back to OPEN", circuitKey);
                    var (cbCluster, cbDest) = InMemoryCircuitStateStore.ParseCircuitKey(circuitKey);
                    _notificationService.NotifyCircuitBreakerOpen(cbCluster, cbDest);
                }
                else if (state.ConsecutiveFailures >= state.FailureThreshold)
                {
                    state.Status = CircuitStatus.Open;
                    state.OpenedAt = DateTime.UtcNow;
                    _logger.LogWarning("Circuit OPENED for {CircuitKey} after {Failures} failures", circuitKey, state.ConsecutiveFailures);
                    _notificationService.NotifyCircuitBreakerOpen(state.ClusterKeySnapshot, state.DestinationKeySnapshot);
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
        var now = DateTime.UtcNow;
        var lastTicks = Interlocked.Read(ref _lastCleanupTicks);
        if (now - new DateTime(lastTicks, DateTimeKind.Utc) < _cleanupThreshold)
            return;

        Interlocked.Exchange(ref _lastCleanupTicks, now.Ticks);
        _circuitStore.CleanupStale(_cleanupThreshold);
    }
}

public enum CircuitStatus { Closed, Open, HalfOpen }

/// <summary>Internal action determined by circuit breaker lock evaluation.</summary>
internal enum CircuitAction { Proceed, RejectOpen, RejectHalfOpen }

public class CircuitState
{
    /// <summary>Per-circuit lock object for protecting all state transitions under concurrency.</summary>
    public readonly object SyncRoot = new();
    public string ClusterUid { get; set; } = string.Empty;
    public string ClusterKeySnapshot { get; set; } = string.Empty;
    public string DestinationUid { get; set; } = "any";
    public string DestinationKeySnapshot { get; set; } = "any";
    public CircuitStatus Status { get; set; } = CircuitStatus.Closed;
    public int ConsecutiveFailures { get; set; }
    public int FailureThreshold { get; set; }
    public TimeSpan RecoveryTimeout { get; set; }
    public int MaxHalfOpenAttempts { get; set; }
    public DateTime OpenedAt { get; set; }
    public int HalfOpenRequests { get; set; }
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;

    public CircuitState(CircuitBreakerOptions? options = null)
    {
        ApplyOptions(options ?? new CircuitBreakerOptions());
    }

    public void ApplyOptions(CircuitBreakerOptions options)
    {
        FailureThreshold = options.DefaultFailureThreshold > 0 ? options.DefaultFailureThreshold : 5;
        RecoveryTimeout = TimeSpan.FromSeconds(options.DefaultRecoveryTimeoutSeconds > 0 ? options.DefaultRecoveryTimeoutSeconds : 30);
        MaxHalfOpenAttempts = options.HalfOpenMaxAttempts > 0 ? options.HalfOpenMaxAttempts : 1;
    }
}

public class CircuitStateInfo
{
    public string Key { get; set; } = string.Empty;
    public string ClusterUid { get; set; } = string.Empty;
    public string ClusterKeySnapshot { get; set; } = string.Empty;
    public string ClusterName { get; set; } = string.Empty;
    public string DestinationUid { get; set; } = "any";
    public string DestinationKeySnapshot { get; set; } = "any";
    public string Status { get; set; } = "Closed";
    public int ConsecutiveFailures { get; set; }
    public int FailureThreshold { get; set; }
    public TimeSpan RecoveryTimeout { get; set; }
    public int RecoveryTimeoutSeconds { get; set; }
    public int HalfOpenRequests { get; set; }
    public int MaxHalfOpenAttempts { get; set; }
    public DateTime? OpenedAt { get; set; }
    public DateTime LastAccessedAt { get; set; }
}
