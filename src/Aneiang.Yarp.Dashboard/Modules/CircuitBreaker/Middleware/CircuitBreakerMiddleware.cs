using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Aneiang.Yarp.Dashboard.Modules.Notification.Services;
using System.Collections.Concurrent;
using Yarp.ReverseProxy.Model;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Models;

namespace Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;

/// <summary>
/// Per-destination circuit breaker middleware.
/// Reads configuration from cluster-level CircuitBreakerConfig.
/// Tracks consecutive failures and opens the circuit when threshold is reached.
/// States: Closed → Open → HalfOpen → Closed.
/// </summary>
public sealed class CircuitBreakerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CircuitBreakerMiddleware> _logger;
    private readonly CircuitBreakerOptions _options;
    private readonly IGatewayPluginManager _pluginManager;
    private readonly IDynamicYarpConfigService _yarpConfig;
    private readonly string _dashPrefix;
    private readonly INotificationService _notificationService;

    private const string ContentRoot = "/_content/Aneiang.Yarp.Dashboard";

    private static readonly ConcurrentDictionary<string, CircuitState> _circuits = new();
    private static readonly object _stateLock = new();

    private static readonly TimeSpan _cleanupThreshold = TimeSpan.FromHours(3);
    private static DateTime _lastCleanupTime = DateTime.Now;

    /// <summary>
    /// Ensure a circuit entry exists for clusters that have CB enabled,
    /// so that the dashboard can show them even before any traffic arrives.
    /// </summary>
    public static void EnsureCircuitExists(string clusterId, CircuitBreakerConfig cbConfig, string? clusterUid = null)
    {
        var circuitKey = BuildCircuitKey(clusterUid, clusterId, null);
        var options = ToOptions(cbConfig, maxCircuitCount: 1000);
        var state = _circuits.GetOrAdd(circuitKey, _ => new CircuitState(options));

        lock (_stateLock)
        {
            state.ApplyOptions(options);
            state.ClusterUid = clusterUid ?? StableUidFromKey("cluster", clusterId);
            state.ClusterKeySnapshot = clusterId;
            state.DestinationUid = "any";
            state.DestinationKeySnapshot = "any";
        }
    }

    public CircuitBreakerMiddleware(
        RequestDelegate next,
        ILogger<CircuitBreakerMiddleware> logger,
        IOptions<CircuitBreakerOptions> options,
        IOptions<DashboardOptions> dashOptions,
        IGatewayPluginManager pluginManager,
        IDynamicYarpConfigService yarpConfig,
        INotificationService? notificationService = null)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;
        _pluginManager = pluginManager;
        _yarpConfig = yarpConfig;
        _dashPrefix = "/" + dashOptions.Value.RoutePrefix.Trim('/');
        _notificationService = notificationService ?? NullNotificationService.Instance;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        if (path.StartsWith(_dashPrefix, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(ContentRoot, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (!_pluginManager.IsPluginEnabled("circuit-breaker"))
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

        var clusterUid = ResolveClusterUid(clusterId);
        var circuitKey = BuildCircuitKey(clusterUid, clusterId, destinationId);

        var cbConfig = GetClusterCircuitBreakerConfig(clusterId);
        if (cbConfig == null || !cbConfig.Enabled)
        {
            await _next(context);
            return;
        }

        var effectiveOptions = GetEffectiveOptions(cbConfig);

        if (_circuits.Count >= _options.MaxCircuitCount && !_circuits.ContainsKey(circuitKey))
        {
            _logger.LogWarning("Circuit count limit reached ({Max}), skipping new circuit for {CircuitKey}",
                _options.MaxCircuitCount, circuitKey);
            await _next(context);
            return;
        }

        var state = _circuits.GetOrAdd(circuitKey, _ => new CircuitState(effectiveOptions));
        lock (_stateLock)
        {
            state.ClusterUid = clusterUid ?? StableUidFromKey("cluster", clusterId);
            state.ClusterKeySnapshot = clusterId;
            state.DestinationUid = ResolveDestinationUid(destinationId);
            state.DestinationKeySnapshot = destinationId ?? "any";
        }
        state.LastAccessedAt = DateTime.Now;

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
            UpdateCircuitState(state, circuitKey, context.Response.StatusCode, cbConfig);
        }
    }

    private CircuitBreakerConfig? GetClusterCircuitBreakerConfig(string clusterId)
    {
        var dynConfig = _yarpConfig.GetDynamicConfig();
        return dynConfig?.Clusters.FirstOrDefault(c =>
            string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase))
            ?.CircuitBreaker;
    }

    private string? ResolveClusterUid(string clusterId)
    {
        var dynConfig = _yarpConfig.GetDynamicConfig();
        return dynConfig?.Clusters.FirstOrDefault(c =>
            string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase))
            ?.ClusterUid;
    }

    private CircuitBreakerOptions GetEffectiveOptions(CircuitBreakerConfig cbConfig) =>
        ToOptions(cbConfig, _options.MaxCircuitCount);

    private static CircuitBreakerOptions ToOptions(CircuitBreakerConfig cbConfig, int maxCircuitCount)
    {
        return new CircuitBreakerOptions
        {
            Enabled = cbConfig.Enabled,
            DefaultFailureThreshold = cbConfig.FailureThreshold > 0 ? cbConfig.FailureThreshold : 5,
            DefaultRecoveryTimeoutSeconds = cbConfig.RecoveryTimeoutSeconds > 0 ? cbConfig.RecoveryTimeoutSeconds : 30,
            HalfOpenMaxAttempts = cbConfig.HalfOpenMaxAttempts > 0 ? cbConfig.HalfOpenMaxAttempts : 1,
            MaxCircuitCount = maxCircuitCount
        };
    }

    private void UpdateCircuitState(CircuitState state, string circuitKey, int statusCode, CircuitBreakerConfig cbConfig)
    {
        var isFailure = cbConfig.FailureStatusCodes.Contains(statusCode) || statusCode >= 500;

        lock (_stateLock)
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
                    var (cbCluster, cbDest) = ParseCircuitKey(circuitKey);
                    _notificationService.NotifyCircuitBreakerOpen(cbCluster, cbDest);
                }
                else if (state.ConsecutiveFailures >= state.FailureThreshold)
                {
                    state.Status = CircuitStatus.Open;
                    state.OpenedAt = DateTime.Now;
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

    /// <summary>Get circuit breaker state for all circuits (for dashboard).</summary>
    public static IReadOnlyList<CircuitStateInfo> GetAllCircuitStates()
    {
        return _circuits.Select(kv => new CircuitStateInfo
        {
            Key = kv.Key,
            ClusterUid = kv.Value.ClusterUid,
            ClusterKeySnapshot = kv.Value.ClusterKeySnapshot,
            DestinationUid = kv.Value.DestinationUid,
            DestinationKeySnapshot = kv.Value.DestinationKeySnapshot,
            Status = kv.Value.Status.ToString(),
            ConsecutiveFailures = kv.Value.ConsecutiveFailures,
            FailureThreshold = kv.Value.FailureThreshold,
            RecoveryTimeout = kv.Value.RecoveryTimeout,
            RecoveryTimeoutSeconds = (int)kv.Value.RecoveryTimeout.TotalSeconds,
            HalfOpenRequests = kv.Value.HalfOpenRequests,
            MaxHalfOpenAttempts = kv.Value.MaxHalfOpenAttempts,
            OpenedAt = kv.Value.OpenedAt == DateTime.MinValue ? null : kv.Value.OpenedAt,
            LastAccessedAt = kv.Value.LastAccessedAt
        }).ToList();
    }

    /// <summary>Remove circuit entries for a cluster (used when policy is unapplied).</summary>
    public static void RemoveCircuitsForCluster(string clusterId, string? clusterUid = null)
    {
        var keysToRemove = _circuits
            .Where(kv =>
                string.Equals(kv.Value.ClusterKeySnapshot, clusterId, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(clusterUid)
                    && string.Equals(kv.Value.ClusterUid, clusterUid, StringComparison.OrdinalIgnoreCase)))
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _circuits.TryRemove(key, out _);
        }
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

    /// <summary>Update cluster key snapshots after a cluster key changes. UID-based keys remain stable.</summary>
    public static void RenameClusterKey(string oldClusterId, string newClusterId)
    {
        foreach (var state in _circuits.Values)
        {
            if (!string.Equals(state.ClusterKeySnapshot, oldClusterId, StringComparison.OrdinalIgnoreCase)) continue;
            lock (_stateLock)
            {
                state.ClusterKeySnapshot = newClusterId;
            }
        }
    }

    /// <summary>
    /// Check if a specific circuit is open for a cluster/destination.
    /// Used by retry middleware to skip unhealthy destinations.
    /// </summary>
    public static bool IsCircuitOpen(string clusterId, string? destinationId = null, string? clusterUid = null)
    {
        var key = BuildCircuitKey(clusterUid, clusterId, destinationId);
        if (!_circuits.TryGetValue(key, out var state))
        {
            var legacyKey = $"{clusterId}:{destinationId ?? "any"}";
            _circuits.TryGetValue(legacyKey, out state);
        }

        if (state == null) return false;
        lock (_stateLock)
        {
            return state.Status == CircuitStatus.Open;
        }
    }

    private static string BuildCircuitKey(string? clusterUid, string clusterKey, string? destinationKey)
    {
        var resolvedClusterUid = string.IsNullOrWhiteSpace(clusterUid) ? StableUidFromKey("cluster", clusterKey) : clusterUid;
        return $"{resolvedClusterUid}:{ResolveDestinationUid(destinationKey)}";
    }

    private static string ResolveDestinationUid(string? destinationKey)
        => string.IsNullOrWhiteSpace(destinationKey) ? "any" : StableUidFromKey("destination", destinationKey);

    private static string StableUidFromKey(string prefix, string key)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(prefix + ":" + key));
        return Convert.ToHexString(bytes, 0, 16).ToLowerInvariant();
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

    private void TryCleanupStaleCircuits()
    {
        var now = DateTime.Now;
        if (now - _lastCleanupTime < _cleanupThreshold)
            return;

        _lastCleanupTime = now;

        var keysToRemove = new List<string>();
        foreach (var kv in _circuits)
        {
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
    public DateTime LastAccessedAt { get; set; } = DateTime.Now;

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
