using System;

namespace Aneiang.Yarp.Storage.Abstractions.Storage.Entities;

/// <summary>
/// Persisted runtime state for a plugin target (circuit breaker state, rate-limit
/// statistics, WAF block counts, retry counts, service-discovery status, etc.).
/// Configuration data lives in <c>plugin_bindings</c>; this table stores only
/// volatile runtime state that should survive process restarts.
/// </summary>
public class PluginRuntimeStateEntity
{
    /// <summary>Plugin identifier, e.g. "circuit-breaker", "rate-limit".</summary>
    public string PluginId { get; set; } = string.Empty;

    /// <summary>Target scope: "route", "cluster", "destination", "global".</summary>
    public string TargetType { get; set; } = string.Empty;

    /// <summary>UID of the target route/cluster/destination, or "*" for global.</summary>
    public string TargetUid { get; set; } = string.Empty;

    /// <summary>Gateway node identifier (for multi-node setups).</summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>JSON-serialized runtime state.</summary>
    public string StateJson { get; set; } = "{}";

    /// <summary>UTC timestamp of the last update.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
