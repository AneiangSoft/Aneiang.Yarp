using System.Collections.Concurrent;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Storage;

namespace Aneiang.Yarp.Dashboard.Infrastructure.State;

/// <summary>
/// In-memory singleton implementation of <see cref="ICircuitStateStore"/>.
/// Replaces the static ConcurrentDictionary that was previously in CircuitBreakerMiddleware.
/// </summary>
public sealed class InMemoryCircuitStateStore : ICircuitStateStore
{
    private readonly ConcurrentDictionary<string, CircuitState> _circuits = new();
    private readonly object _stateLock = new();

    public int Count => _circuits.Count;

    public CircuitState GetOrAdd(string key, Func<string, CircuitState> factory)
        => _circuits.GetOrAdd(key, factory);

    public bool TryGet(string key, out CircuitState? state)
    {
        if (_circuits.TryGetValue(key, out state))
            return true;

        // Legacy key fallback
        var lastColon = key.LastIndexOf(':');
        if (lastColon > 0)
        {
            var legacyKey = key[(lastColon + 1)..] == "any"
                ? key[..lastColon] + ":any"
                : key;
            return _circuits.TryGetValue(legacyKey, out state);
        }

        state = null;
        return false;
    }

    public bool ContainsKey(string key) => _circuits.ContainsKey(key);

    public IReadOnlyList<KeyValuePair<string, CircuitState>> GetAll()
        => _circuits.ToList();

    public bool TryRemove(string key) => _circuits.TryRemove(key, out _);

    public void RemoveForCluster(string clusterId, string? clusterUid)
    {
        var keysToRemove = _circuits
            .Where(kv =>
                string.Equals(kv.Value.ClusterKeySnapshot, clusterId, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(clusterUid)
                    && string.Equals(kv.Value.ClusterUid, clusterUid, StringComparison.OrdinalIgnoreCase)))
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in keysToRemove)
            _circuits.TryRemove(key, out _);
    }

    public void ResetAll()
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

    public void RenameClusterKey(string oldClusterId, string newClusterId)
    {
        foreach (var state in _circuits.Values)
        {
            if (!string.Equals(state.ClusterKeySnapshot, oldClusterId, StringComparison.OrdinalIgnoreCase))
                continue;
            lock (_stateLock)
            {
                state.ClusterKeySnapshot = newClusterId;
            }
        }
    }

    public void EnsureCircuitExists(string clusterId, CircuitBreakerConfig cbConfig, string? clusterUid)
    {
        var circuitKey = BuildCircuitKey(clusterUid, clusterId, null);
        var options = ToOptions(cbConfig, maxCircuitCount: 1000);
        var state = _circuits.GetOrAdd(circuitKey, _ => new CircuitState(options));

        lock (_stateLock)
        {
            state.ApplyOptions(options);
            state.ClusterUid = clusterUid ?? StableUid.FromKey("cluster", clusterId);
            state.ClusterKeySnapshot = clusterId;
            state.DestinationUid = "any";
            state.DestinationKeySnapshot = "any";
        }
    }

    public bool IsCircuitOpen(string clusterId, string? destinationId = null, string? clusterUid = null)
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

    public IReadOnlyList<CircuitStateInfo> GetAllStateInfos()
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

    public void CleanupStale(TimeSpan threshold)
    {
        var now = DateTime.UtcNow;
        var keysToRemove = new List<string>();
        foreach (var kv in _circuits)
        {
            if (kv.Value.Status == CircuitStatus.Closed &&
                now - kv.Value.LastAccessedAt > threshold)
            {
                keysToRemove.Add(kv.Key);
            }
        }

        foreach (var key in keysToRemove)
            _circuits.TryRemove(key, out _);
    }

    /// <summary>Acquires the internal lock for state mutations. Use sparingly.</summary>
    internal void Lock(Action<CircuitState> action, CircuitState state)
    {
        lock (_stateLock)
        {
            action(state);
        }
    }

    // ---- Static helpers (pure functions, safe to keep static) ----

    internal static string BuildCircuitKey(string? clusterUid, string clusterKey, string? destinationKey)
    {
        var resolvedClusterUid = string.IsNullOrWhiteSpace(clusterUid)
            ? StableUid.FromKey("cluster", clusterKey) : clusterUid;
        return $"{resolvedClusterUid}:{ResolveDestinationUid(destinationKey)}";
    }

    internal static string ResolveDestinationUid(string? destinationKey)
        => string.IsNullOrWhiteSpace(destinationKey) ? "any" : StableUid.FromKey("destination", destinationKey);

    internal static CircuitBreakerOptions ToOptions(CircuitBreakerConfig cbConfig, int maxCircuitCount)
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

    internal static (string ClusterId, string? DestinationId) ParseCircuitKey(string key)
    {
        var lastColon = key.LastIndexOf(':');
        if (lastColon < 0) return (key, null);
        var cluster = key[..lastColon];
        var dest = key[(lastColon + 1)..];
        return dest == "any" ? (cluster, null) : (cluster, dest);
    }
}
