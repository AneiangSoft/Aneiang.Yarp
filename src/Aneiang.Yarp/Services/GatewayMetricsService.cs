using System.Collections.Concurrent;
using System.Text;
using Aneiang.Yarp.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Singleton service that collects and exports YARP gateway metrics in Prometheus exposition format.
/// Thread-safe via ConcurrentDictionary and Interlocked operations.
/// </summary>
public sealed class GatewayMetricsService
{
    private readonly GatewayMetricsOptions _options;
    private readonly ILogger<GatewayMetricsService> _logger;
    private readonly double[] _buckets;

    private static readonly ConcurrentDictionary<string, CounterValue> _requestCounters = new();
    private static readonly ConcurrentDictionary<string, HistogramValue> _durationHistograms = new();
    private static readonly ConcurrentDictionary<string, GaugeValue> _activeRequests = new();

    public GatewayMetricsService(IOptions<GatewayMetricsOptions> options, ILogger<GatewayMetricsService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _buckets = _options.DurationBuckets.Length > 0
            ? _options.DurationBuckets.OrderBy(b => b).ToArray()
            : [5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000];
    }

    /// <summary>
    /// Record a completed proxy request.
    /// </summary>
    /// <param name="routeId">Route identifier.</param>
    /// <param name="clusterId">Cluster identifier.</param>
    /// <param name="method">HTTP method.</param>
    /// <param name="statusCode">Response status code.</param>
    /// <param name="durationMs">Request duration in milliseconds.</param>
    public void RecordRequest(string routeId, string clusterId, string method, int statusCode, double durationMs)
    {
        if (!_options.Enabled) return;

        var route = _options.EnablePerRouteMetrics ? routeId : "*";

        // Counter
        var counterKey = $"{route}:{clusterId}:{method}:{statusCode}";
        var counter = _requestCounters.GetOrAdd(counterKey, _ => new CounterValue
        {
            Route = route,
            Cluster = clusterId,
            Method = method,
            StatusCode = statusCode
        });
        Interlocked.Increment(ref counter.Count);

        // Histogram
        var histKey = $"{route}:{clusterId}:{method}";
        var hist = _durationHistograms.GetOrAdd(histKey, _ =>
        {
            var h = new HistogramValue
            {
                Route = route,
                Cluster = clusterId,
                Method = method,
                BucketUpperBounds = _buckets,
                BucketCounts = new long[_buckets.Length]
            };
            return h;
        });
        hist.Observe(durationMs);

        _logger.LogDebug(
            "Recorded request: route={Route}, cluster={Cluster}, method={Method}, status={Status}, duration={Duration}ms",
            route, clusterId, method, statusCode, durationMs);
    }

    /// <summary>
    /// Increment the active requests gauge for a route/cluster.
    /// </summary>
    public void IncrementActiveRequests(string routeId, string clusterId)
    {
        if (!_options.Enabled) return;
        var route = _options.EnablePerRouteMetrics ? routeId : "*";
        var key = $"{route}:{clusterId}";
        var gauge = _activeRequests.GetOrAdd(key, _ => new GaugeValue
        {
            Route = route,
            Cluster = clusterId
        });
        Interlocked.Increment(ref gauge.Count);
    }

    /// <summary>
    /// Decrement the active requests gauge for a route/cluster.
    /// </summary>
    public void DecrementActiveRequests(string routeId, string clusterId)
    {
        if (!_options.Enabled) return;
        var route = _options.EnablePerRouteMetrics ? routeId : "*";
        var key = $"{route}:{clusterId}";
        if (_activeRequests.TryGetValue(key, out var gauge))
        {
            Interlocked.Decrement(ref gauge.Count);
        }
    }

    /// <summary>
    /// Render all collected metrics in Prometheus exposition text format.
    /// </summary>
    public string GetPrometheusText()
    {
        var sb = new StringBuilder();

        // ── yarp_requests_total (Counter) ───────────────────────
        sb.AppendLine("# HELP yarp_requests_total Total number of proxy requests.");
        sb.AppendLine("# TYPE yarp_requests_total counter");

        foreach (var kv in _requestCounters.OrderBy(k => k.Key))
        {
            var c = kv.Value;
            sb.AppendLine($"yarp_requests_total{{route=\"{EscapeLabel(c.Route)}\",cluster=\"{EscapeLabel(c.Cluster)}\",method=\"{EscapeLabel(c.Method)}\",status_code=\"{c.StatusCode}\"}} {Volatile.Read(ref c.Count)}");
        }

        // ── yarp_request_duration_ms (Histogram) ────────────────
        sb.AppendLine();
        sb.AppendLine("# HELP yarp_request_duration_ms Request duration in milliseconds.");
        sb.AppendLine("# TYPE yarp_request_duration_ms histogram");

        foreach (var kv in _durationHistograms.OrderBy(k => k.Key))
        {
            var h = kv.Value;
            var labelBase = $"route=\"{EscapeLabel(h.Route)}\",cluster=\"{EscapeLabel(h.Cluster)}\",method=\"{EscapeLabel(h.Method)}\"";

            long cumulative = 0;
            for (int i = 0; i < h.BucketUpperBounds.Length; i++)
            {
                cumulative += Volatile.Read(ref h.BucketCounts[i]);
                sb.AppendLine($"yarp_request_duration_ms_bucket{{{labelBase},le=\"{h.BucketUpperBounds[i]}\"}} {cumulative}");
            }
            // +Inf bucket
            sb.AppendLine($"yarp_request_duration_ms_bucket{{{labelBase},le=\"+Inf\"}} {Volatile.Read(ref h.TotalCount)}");

            // Sum
            sb.AppendLine($"yarp_request_duration_ms_sum{{{labelBase}}} {Volatile.Read(ref h.Sum):0.###}");
            // Count
            sb.AppendLine($"yarp_request_duration_ms_count{{{labelBase}}} {Volatile.Read(ref h.TotalCount)}");
        }

        // ── yarp_active_requests (Gauge) ─────────────────────────
        sb.AppendLine();
        sb.AppendLine("# HELP yarp_active_requests Number of active proxy requests currently being processed.");
        sb.AppendLine("# TYPE yarp_active_requests gauge");

        foreach (var kv in _activeRequests.OrderBy(k => k.Key))
        {
            var g = kv.Value;
            sb.AppendLine($"yarp_active_requests{{route=\"{EscapeLabel(g.Route)}\",cluster=\"{EscapeLabel(g.Cluster)}\"}} {Volatile.Read(ref g.Count)}");
        }

        return sb.ToString();
    }

    private static string EscapeLabel(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
    }

    // ── Internal value types ──────────────────────────────────────

    private sealed class CounterValue
    {
        public long Count;
        public string Route = string.Empty;
        public string Cluster = string.Empty;
        public string Method = string.Empty;
        public int StatusCode;
    }

    private sealed class HistogramValue
    {
        public double[] BucketUpperBounds = [];
        public long[] BucketCounts = [];
        public long TotalCount;
        public double Sum;
        public string Route = string.Empty;
        public string Cluster = string.Empty;
        public string Method = string.Empty;
        private readonly object _sumLock = new();

        public void Observe(double value)
        {
            for (int i = 0; i < BucketUpperBounds.Length; i++)
            {
                if (value <= BucketUpperBounds[i])
                {
                    Interlocked.Increment(ref BucketCounts[i]);
                }
            }
            Interlocked.Increment(ref TotalCount);

            lock (_sumLock)
            {
                Sum += value;
            }
        }
    }

    private sealed class GaugeValue
    {
        public long Count;
        public string Route = string.Empty;
        public string Cluster = string.Empty;
    }
}
