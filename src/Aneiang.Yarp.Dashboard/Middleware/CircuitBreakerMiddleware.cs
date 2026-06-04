using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Dashboard.Services;
using System.Collections.Concurrent;
using System.Text.Json;
using Yarp.ReverseProxy.Model;

namespace Aneiang.Yarp.Dashboard.Middleware;

/// <summary>
/// Per-destination circuit breaker middleware.
/// Tracks consecutive failures and opens the circuit when threshold is reached.
/// States: Closed → Open → HalfOpen → Closed.
/// </summary>
public sealed class CircuitBreakerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CircuitBreakerMiddleware> _logger;
    private readonly CircuitBreakerOptions _options;
    private readonly IGatewayAlertService _alertService;
    private readonly string _dashPrefix;
    /// <summary>
    /// Content root path for the Dashboard static files. Used to skip logging for frontend resources.
    /// </summary>
    private const string ContentRoot = "/_content/Aneiang.Yarp.Dashboard";

    private static readonly ConcurrentDictionary<string, CircuitState> _circuits = new();
    private static readonly object _stateLock = new();

    // Cleanup circuits that have been closed and not accessed for this duration
    private static readonly TimeSpan _cleanupThreshold = TimeSpan.FromHours(1);
    private static DateTime _lastCleanupTime = DateTime.Now;

    public CircuitBreakerMiddleware(
        RequestDelegate next,
        ILogger<CircuitBreakerMiddleware> logger,
        IOptions<CircuitBreakerOptions> options,
        IOptions<DashboardOptions> dashOptions,
        IGatewayAlertService alertService)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;
        _alertService = alertService;
        _dashPrefix = "/" + dashOptions.Value.RoutePrefix.Trim('/');
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip Dashboard UI and API requests - they should not go through circuit breaker
        var path = context.Request.Path.Value ?? "";
        if (path.StartsWith(_dashPrefix, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(ContentRoot, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var proxyFeature = context.Features.Get<IReverseProxyFeature>();
        var clusterId = proxyFeature?.Route?.Config?.ClusterId;
        var destinationId = proxyFeature?.ProxiedDestination?.DestinationId;

        if (string.IsNullOrEmpty(clusterId))
        {
            await _next(context);
            return;
        }

        var circuitKey = $"{clusterId}:{destinationId ?? "any"}";

        if (!IsCircuitBreakerEnabled(proxyFeature))
        {
            await _next(context);
            return;
        }

        var options = GetEffectiveOptions(proxyFeature);

        // Enforce max circuit count to prevent memory exhaustion
        if (_circuits.Count >= _options.MaxCircuitCount && !_circuits.ContainsKey(circuitKey))
        {
            _logger.LogWarning("Circuit count limit reached ({Max}), skipping new circuit for {CircuitKey}",
                _options.MaxCircuitCount, circuitKey);
            await _next(context);
            return;
        }

        var state = _circuits.GetOrAdd(circuitKey, _ => new CircuitState(options));
        state.LastAccessedAt = DateTime.Now;

        // Periodic cleanup of stale closed circuits
        TryCleanupStaleCircuits();

        CircuitStatus currentStatus;
        lock (_stateLock)
        {
            currentStatus = state.Status;
        }

        if (currentStatus == CircuitStatus.Open)
        {
            if (DateTime.Now < state.OpenedAt + state.RecoveryTimeout)
            {
                _logger.LogWarning(
                    "Circuit OPEN for {CircuitKey} (recovery at {RecoveryAt})",
                    circuitKey, state.OpenedAt + state.RecoveryTimeout);

                context.Response.StatusCode = 503;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "ServiceUnavailable",
                    message = $"Circuit breaker open for cluster '{clusterId}'. Retry later."
                });
                return;
            }

            lock (_stateLock)
            {
                state.Status = CircuitStatus.HalfOpen;
                state.HalfOpenRequests = 0;
            }
            _logger.LogInformation("Circuit HALF-OPEN for {CircuitKey}", circuitKey);
        }

        bool rejectHalfOpen = false;
        if (state.Status == CircuitStatus.HalfOpen)
        {
            lock (_stateLock)
            {
                if (state.HalfOpenRequests >= state.MaxHalfOpenAttempts)
                {
                    rejectHalfOpen = true;
                }
                else
                {
                    state.HalfOpenRequests++;
                }
            }
        }

        if (rejectHalfOpen)
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
            await _next(context);
        }
        finally
        {
            UpdateCircuitState(state, circuitKey, context.Response.StatusCode);
        }
    }

    private void UpdateCircuitState(CircuitState state, string circuitKey, int statusCode)
    {
        lock (_stateLock)
        {
            state.LastAccessedAt = DateTime.Now;

            if (statusCode >= 500)
            {
                state.ConsecutiveFailures++;

                if (state.Status == CircuitStatus.HalfOpen)
                {
                    state.Status = CircuitStatus.Open;
                    state.OpenedAt = DateTime.Now;
                    _logger.LogWarning("Circuit HALF-OPEN probe FAILED for {CircuitKey}, back to OPEN", circuitKey);
                    // Fire alert on HalfOpen probe failure
                    var (cbCluster, cbDest) = ParseCircuitKey(circuitKey);
                    _alertService.AlertCircuitBreakerOpen(cbCluster, cbDest);
                }
                else if (state.ConsecutiveFailures >= state.FailureThreshold)
                {
                    state.Status = CircuitStatus.Open;
                    state.OpenedAt = DateTime.Now;
                    _logger.LogWarning("Circuit OPENED for {CircuitKey} after {Failures} failures", circuitKey, state.ConsecutiveFailures);
                    // Fire alert when circuit first opens due to failure threshold
                    var (cbCluster, cbDest) = ParseCircuitKey(circuitKey);
                    _alertService.AlertCircuitBreakerOpen(cbCluster, cbDest);
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

    private static bool IsCircuitBreakerEnabled(IReverseProxyFeature? proxyFeature)
    {
        if (proxyFeature?.Route?.Config?.Metadata != null &&
            proxyFeature.Route.Config.Metadata.TryGetValue("CircuitBreaker:Enabled", out var enabled) &&
            bool.TryParse(enabled, out var isEnabled))
        {
            return isEnabled;
        }
        return true;
    }

    private CircuitBreakerOptions GetEffectiveOptions(IReverseProxyFeature? proxyFeature)
    {
        var meta = proxyFeature?.Route?.Config?.Metadata;
        
        // Start with global defaults
        var options = new CircuitBreakerOptions
        {
            Enabled = _options.Enabled,
            DefaultFailureThreshold = _options.DefaultFailureThreshold,
            DefaultRecoveryTimeoutSeconds = _options.DefaultRecoveryTimeoutSeconds,
            HalfOpenMaxAttempts = _options.HalfOpenMaxAttempts,
            MaxCircuitCount = _options.MaxCircuitCount
        };

        if (meta == null)
            return options;

        // Override from route metadata
        if (meta.TryGetValue("CircuitBreaker:FailureThreshold", out var threshold) &&
            int.TryParse(threshold, out var t))
        {
            options.DefaultFailureThreshold = t;
        }

        if (meta.TryGetValue("CircuitBreaker:RecoveryTimeoutSeconds", out var timeout) &&
            int.TryParse(timeout, out var to))
        {
            options.DefaultRecoveryTimeoutSeconds = to;
        }

        if (meta.TryGetValue("CircuitBreaker:HalfOpenMaxAttempts", out var halfOpen) &&
            int.TryParse(halfOpen, out var ho))
        {
            options.HalfOpenMaxAttempts = ho;
        }

        return options;
    }

    /// <summary>Get circuit breaker state for all circuits (for dashboard).</summary>
    public static IReadOnlyDictionary<string, CircuitStateInfo> GetAllCircuitStates()
    {
        return _circuits.ToDictionary(
            kv => kv.Key,
            kv => new CircuitStateInfo
            {
                Key = kv.Key,
                Status = kv.Value.Status.ToString(),
                ConsecutiveFailures = kv.Value.ConsecutiveFailures,
                FailureThreshold = kv.Value.FailureThreshold,
                RecoveryTimeout = kv.Value.RecoveryTimeout,
                HalfOpenRequests = kv.Value.HalfOpenRequests,
                MaxHalfOpenAttempts = kv.Value.MaxHalfOpenAttempts,
                OpenedAt = kv.Value.OpenedAt == DateTime.MinValue ? null : kv.Value.OpenedAt,
                LastAccessedAt = kv.Value.LastAccessedAt
            });
    }

    /// <summary>Reset all circuits.</summary>
    public static void ResetAll()
    {
        foreach (var kv in _circuits)
        {
            lock (_stateLock)
            {
                kv.Value.Status = CircuitStatus.Closed;
                kv.Value.ConsecutiveFailures = 0;
                kv.Value.HalfOpenRequests = 0;
            }
        }
    }

    /// <summary>
    /// Check if a specific circuit is open for a cluster/destination.
    /// Used by retry middleware to skip unhealthy destinations.
    /// </summary>
    public static bool IsCircuitOpen(string clusterId, string? destinationId = null)
    {
        var key = $"{clusterId}:{destinationId ?? "any"}";
        if (_circuits.TryGetValue(key, out var state))
        {
            lock (_stateLock)
            {
                return state.Status == CircuitStatus.Open;
            }
        }
        return false;
    }

    private static (string ClusterId, string? DestinationId) ParseCircuitKey(string key)
    {
        var lastColon = key.LastIndexOf(':');
        if (lastColon < 0)
            return (key, null);
        var cluster = key[..lastColon];
        var dest = key[(lastColon + 1)..];
        return dest == "any" ? (cluster, null) : (cluster, dest);
    }

    /// <summary>
    /// Removes stale closed circuits that haven't been accessed for a while.
    /// Runs infrequently to avoid performance impact.
    /// </summary>
    private void TryCleanupStaleCircuits()
    {
        var now = DateTime.Now;
        if (now - _lastCleanupTime < _cleanupThreshold)
            return;

        _lastCleanupTime = now;

        var keysToRemove = new List<string>();
        foreach (var kv in _circuits)
        {
            // Only remove closed circuits that haven't been accessed recently
            if (kv.Value.Status == CircuitStatus.Closed &&
                now - kv.Value.LastAccessedAt > _cleanupThreshold)
            {
                keysToRemove.Add(kv.Key);
            }
        }

        foreach (var key in keysToRemove)
        {
            _circuits.TryRemove(key, out _);
        }
    }
}

internal enum CircuitStatus { Closed, Open, HalfOpen }

internal class CircuitState
{
    public CircuitStatus Status { get; set; } = CircuitStatus.Closed;
    public int ConsecutiveFailures { get; set; }
    public int FailureThreshold { get; set; }
    public TimeSpan RecoveryTimeout { get; set; }
    public int MaxHalfOpenAttempts { get; set; }
    public DateTime OpenedAt { get; set; }
    public int HalfOpenRequests { get; set; }
    public DateTime LastAccessedAt { get; set; } = DateTime.Now;

    public CircuitState(CircuitBreakerOptions? options = null)
    {
        var opts = options ?? new CircuitBreakerOptions();
        FailureThreshold = opts.DefaultFailureThreshold;
        RecoveryTimeout = TimeSpan.FromSeconds(opts.DefaultRecoveryTimeoutSeconds);
        MaxHalfOpenAttempts = opts.HalfOpenMaxAttempts;
    }
}

public class CircuitStateInfo
{
    public string Key { get; set; } = string.Empty;
    public string Status { get; set; } = "Closed";
    public int ConsecutiveFailures { get; set; }
    public int FailureThreshold { get; set; }
    public TimeSpan RecoveryTimeout { get; set; }
    public int HalfOpenRequests { get; set; }
    public int MaxHalfOpenAttempts { get; set; }
    public DateTime? OpenedAt { get; set; }
    public DateTime LastAccessedAt { get; set; }
}
