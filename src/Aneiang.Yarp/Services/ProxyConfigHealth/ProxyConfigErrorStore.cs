using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Services.ProxyConfigHealth;

/// <summary>
/// Singleton, thread-safe implementation of <see cref="IProxyConfigErrorStore"/>.
/// Keeps at most <see cref="MaxErrors"/> most-recent errors in memory.
/// </summary>
public sealed class ProxyConfigErrorStore : IProxyConfigErrorStore
{
    private const int MaxErrors = 50;

    private readonly object _gate = new();
    private readonly LinkedList<ProxyConfigError> _errors = new();
    private readonly ILogger<ProxyConfigErrorStore> _logger;

    private ProxyConfigHealthStatus _status = ProxyConfigHealthStatus.Healthy;
    private DateTime? _lastErrorAt;
    private DateTime? _lastHealthyAt;

    public ProxyConfigErrorStore(ILogger<ProxyConfigErrorStore> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public ProxyConfigHealthSnapshot GetHealth()
    {
        lock (_gate)
        {
            var effective = Dedupe(_errors);
            return new ProxyConfigHealthSnapshot
            {
                Status = _status == ProxyConfigHealthStatus.Error ? "error" : "healthy",
                ErrorCount = effective.Count,
                LastErrorAt = _lastErrorAt,
                LastHealthyAt = _lastHealthyAt,
                // Dedupe returns most-recent-first; FirstError is the latest captured fault.
                FirstError = _status == ProxyConfigHealthStatus.Error && effective.Count > 0
                    ? effective[0]
                    : null
            };
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ProxyConfigError> GetErrors(DateTime? since = null, string? scope = null)
    {
        lock (_gate)
        {
            IEnumerable<ProxyConfigError> result = _errors;
            if (since.HasValue)
            {
                result = result.Where(e => e.OccurredAt >= since.Value);
            }
            if (!string.IsNullOrEmpty(scope))
            {
                if (string.Equals(scope, "route", StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Where(e => !string.IsNullOrEmpty(e.RouteId));
                }
                else if (string.Equals(scope, "cluster", StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Where(e => !string.IsNullOrEmpty(e.ClusterId));
                }
            }
            // Dedupe across sources (reload + prevalidate may capture the same fault twice;
            // keep the authoritative reload capture, drop the redundant prevalidate one),
            // then most recent first.
            return Dedupe(result.ToList());
        }
    }

    /// <summary>
    /// Collapse errors that the reload listener and the prevalidate pre-check captured for
    /// the same fault. Dedupe key is (clusterId, routeId, message); when both sources report
    /// the identical triple, the authoritative <c>"reload"</c> entry wins (it reflects the
    /// actual YARP apply outcome) and the matching <c>"prevalidate"</c> entry is dropped.
    /// The store itself keeps the raw per-source records for diagnostics; only the query
    /// result is collapsed so the UI never shows the same fault twice.
    /// </summary>
    private static IReadOnlyList<ProxyConfigError> Dedupe(IEnumerable<ProxyConfigError> errors)
    {
        var list = errors as IReadOnlyList<ProxyConfigError> ?? errors.ToList();
        if (list.Count <= 1) return list;

        // reload is the authoritative apply result → highest priority (lowest rank).
        var priority = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["reload"] = 0,
            ["prevalidate"] = 1
        };

        var seen = new Dictionary<string, ProxyConfigError>(StringComparer.Ordinal);
        foreach (var err in list.OrderBy(e =>
            priority.TryGetValue(e.Source ?? string.Empty, out var p) ? p : int.MaxValue))
        {
            var key = $"{err.ClusterId ?? string.Empty}\u0001{err.RouteId ?? string.Empty}\u0001{err.Message ?? string.Empty}";
            if (!seen.ContainsKey(key))
            {
                seen[key] = err;
            }
        }
        return seen.Values.OrderByDescending(e => e.OccurredAt).ToList();
    }

    /// <inheritdoc />
    public void ReplaceErrors(string source, IEnumerable<ProxyConfigError> errors)
    {
        if (string.IsNullOrEmpty(source)) return;

        var newErrors = (errors ?? Enumerable.Empty<ProxyConfigError>()).ToList();
        var latestAt = newErrors.Count > 0
            ? newErrors.Max(e => e.OccurredAt)
            : (DateTime?)null;

        lock (_gate)
        {
            // Remove existing errors of this source.
            var node = _errors.First;
            while (node != null)
            {
                var next = node.Next;
                if (string.Equals(node.Value.Source, source, StringComparison.OrdinalIgnoreCase))
                {
                    _errors.Remove(node);
                }
                node = next;
            }

            // Append new ones (preserving insertion order; trim oldest when over capacity).
            foreach (var err in newErrors)
            {
                _errors.AddLast(err);
            }
            while (_errors.Count > MaxErrors)
            {
                _errors.RemoveFirst();
            }

            // Recompute status: error if anything remains, otherwise healthy.
            _status = _errors.Count > 0 ? ProxyConfigHealthStatus.Error : ProxyConfigHealthStatus.Healthy;
            if (latestAt.HasValue && latestAt.Value > (_lastErrorAt ?? DateTime.MinValue))
            {
                _lastErrorAt = latestAt;
            }
        }

        if (newErrors.Count > 0)
        {
            _logger.LogWarning(
                "Captured {Count} proxy config error(s) from source={Source}.",
                newErrors.Count, source);
        }
    }

    /// <inheritdoc />
    public void MarkHealthy()
    {
        lock (_gate)
        {
            _errors.Clear();
            _status = ProxyConfigHealthStatus.Healthy;
            _lastHealthyAt = DateTime.UtcNow;
        }

        _logger.LogInformation("Proxy config applied successfully; error store cleared.");
    }
}
