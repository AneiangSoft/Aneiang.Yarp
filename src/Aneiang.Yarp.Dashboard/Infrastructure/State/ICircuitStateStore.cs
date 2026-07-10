using System.Collections.Concurrent;
using Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;
using Aneiang.Yarp.Models;

namespace Aneiang.Yarp.Dashboard.Infrastructure.State;

/// <summary>
/// Manages in-memory circuit breaker state. Registered as a Singleton so that
/// middleware, controllers, warmup services, and other middleware (retry) all
/// share the same state without relying on static fields.
/// </summary>
public interface ICircuitStateStore
{
    /// <summary>Get an existing circuit or create a new one atomically.</summary>
    CircuitState GetOrAdd(string key, Func<string, CircuitState> factory);

    /// <summary>Try to get a circuit by key, with legacy key fallback.</summary>
    bool TryGet(string key, out CircuitState? state);

    /// <summary>Whether the store contains the given key.</summary>
    bool ContainsKey(string key);

    /// <summary>Current number of circuits.</summary>
    int Count { get; }

    /// <summary>Get all circuit entries as a snapshot list.</summary>
    IReadOnlyList<KeyValuePair<string, CircuitState>> GetAll();

    /// <summary>Remove a circuit by key.</summary>
    bool TryRemove(string key);

    /// <summary>Remove circuits matching a cluster ID or UID.</summary>
    void RemoveForCluster(string clusterId, string? clusterUid);

    /// <summary>Reset all circuits to Closed state.</summary>
    void ResetAll();

    /// <summary>Rename cluster key snapshots after a cluster key change.</summary>
    void RenameClusterKey(string oldClusterId, string newClusterId);

    /// <summary>Ensure a circuit entry exists for a cluster with CB enabled.</summary>
    void EnsureCircuitExists(string clusterId, CircuitBreakerConfig cbConfig, string? clusterUid);

    /// <summary>Check if a specific circuit is open (used by retry middleware).</summary>
    bool IsCircuitOpen(string clusterId, string? destinationId = null, string? clusterUid = null);

    /// <summary>Get all circuits as DTO for dashboard display.</summary>
    IReadOnlyList<CircuitStateInfo> GetAllStateInfos();

    /// <summary>Remove stale closed circuits that haven't been accessed recently.</summary>
    void CleanupStale(TimeSpan threshold);
}
