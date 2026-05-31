using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
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

    private static readonly ConcurrentDictionary<string, CircuitState> _circuits = new();
    private static readonly object _stateLock = new();

    // Cleanup circuits that have been closed and not accessed for this duration
    private static readonly TimeSpan _cleanupThreshold = TimeSpan.FromHours(1);
    private static DateTime _lastCleanupTime = DateTime.Now;

    public CircuitBreakerMiddleware(RequestDelegate next, ILogger<CircuitBreakerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
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

        var state = _circuits.GetOrAdd(circuitKey, _ => new CircuitState());
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
                if (state.HalfOpenRequests >= 1)
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
                OpenedAt = kv.Value.OpenedAt,
                RecoveryTimeout = kv.Value.RecoveryTimeout
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
            }
        }
    }

    /// <summary>
    /// Removes stale closed circuits that haven't been accessed for a while.
    /// Runs infrequently to avoid performance impact.
    /// </summary>
    private static void TryCleanupStaleCircuits()
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
    public CircuitStatus Status;
    public int ConsecutiveFailures;
    public int FailureThreshold = 5;
    public TimeSpan RecoveryTimeout = TimeSpan.FromSeconds(30);
    public DateTime OpenedAt;
    public int HalfOpenRequests;
    public DateTime LastAccessedAt = DateTime.Now; // Track for cleanup
}

public class CircuitStateInfo
{
    public string Key { get; set; } = string.Empty;
    public string Status { get; set; } = "Closed";
    public int ConsecutiveFailures { get; set; }
    public int FailureThreshold { get; set; }
    public DateTime? OpenedAt { get; set; }
    public TimeSpan RecoveryTimeout { get; set; }
}
