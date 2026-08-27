using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Services.ProxyConfigHealth;

/// <summary>
/// Status of the most recent proxy configuration apply attempt.
/// </summary>
public enum ProxyConfigHealthStatus
{
    /// <summary>
    /// No configuration errors are currently known (last apply succeeded or no failure observed).
    /// </summary>
    Healthy = 0,

    /// <summary>
    /// At least one configuration error is currently known; the running proxy may be serving a
    /// stale configuration or no configuration at all.
    /// </summary>
    Error = 1
}

/// <summary>
/// A single captured proxy configuration error.
/// </summary>
public sealed class ProxyConfigError
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Best-effort attributed route id, if the error could be localized to a route.</summary>
    [JsonPropertyName("routeId")]
    public string? RouteId { get; set; }

    /// <summary>Best-effort attributed cluster id, if the error could be localized to a cluster.</summary>
    [JsonPropertyName("clusterId")]
    public string? ClusterId { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("exceptionType")]
    public string? ExceptionType { get; set; }

    [JsonPropertyName("occurredAt")]
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>Origin of the capture: <c>"reload"</c> (YARP listener) or <c>"prevalidate"</c> (publisher pre-check).</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "reload";
}

/// <summary>
/// Compact health summary embedded in the overview snapshot.
/// </summary>
public sealed class ProxyConfigHealthSnapshot
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "healthy";

    [JsonPropertyName("errorCount")]
    public int ErrorCount { get; set; }

    [JsonPropertyName("lastErrorAt")]
    public DateTime? LastErrorAt { get; set; }

    [JsonPropertyName("lastHealthyAt")]
    public DateTime? LastHealthyAt { get; set; }

    /// <summary>Most recent error (for the overview banner summary). Null when healthy.</summary>
    [JsonPropertyName("firstError")]
    public ProxyConfigError? FirstError { get; set; }
}

/// <summary>
/// Thread-safe in-memory store for proxy configuration apply errors.
/// Captures failures observed both by the YARP <c>IConfigChangeListener</c>
/// (real reload outcome) and by the publisher's <c>IConfigValidator</c>
/// pre-check (precise RouteId/ClusterId attribution).
/// </summary>
/// <remarks>
/// The store reflects the LATEST known state per source: each <c>ReplaceErrors</c>
/// call discards previous errors of that source and installs the new set, so the
/// store never accumulates stale errors across reload cycles. A successful apply
/// (<c>MarkHealthy</c>) clears everything.
/// </remarks>
public interface IProxyConfigErrorStore
{
    /// <summary>Current compact health summary.</summary>
    ProxyConfigHealthSnapshot GetHealth();

    /// <summary>
    /// Recent errors, optionally filtered since a timestamp and by scope.
    /// </summary>
    /// <param name="since">If set, only errors at or after this UTC timestamp are returned.</param>
    /// <param name="scope">If <c>"route"</c> only route-attributed errors; if <c>"cluster"</c> only cluster-attributed; otherwise all.</param>
    IReadOnlyList<ProxyConfigError> GetErrors(DateTime? since = null, string? scope = null);

    /// <summary>
    /// Atomically replace all errors tagged with <paramref name="source"/> with
    /// <paramref name="errors"/>. Recomputes health status. An empty sequence clears
    /// that source's errors.
    /// </summary>
    void ReplaceErrors(string source, IEnumerable<ProxyConfigError> errors);

    /// <summary>Mark the proxy configuration as healthy and clear all stored errors.</summary>
    void MarkHealthy();
}
