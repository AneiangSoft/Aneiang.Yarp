namespace Aneiang.Yarp.Models;

/// <summary>
/// Options for Prometheus metrics collection and export.
/// Bound from <c>Gateway:Metrics</c> config section.
/// </summary>
public class GatewayMetricsOptions
{
    /// <summary>Config section name.</summary>
    public const string SectionName = "Gateway:Metrics";

    /// <summary>
    /// Enable Prometheus metrics collection. Default: false.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Metrics endpoint path. Default: "/metrics".
    /// </summary>
    public string EndpointPath { get; set; } = "/metrics";

    /// <summary>
    /// Enable per-route request metrics. Default: true.
    /// </summary>
    public bool EnablePerRouteMetrics { get; set; } = true;

    /// <summary>
    /// Histogram buckets for request duration (in ms).
    /// Default: 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000.
    /// </summary>
    public double[] DurationBuckets { get; set; } = [5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000];
}
