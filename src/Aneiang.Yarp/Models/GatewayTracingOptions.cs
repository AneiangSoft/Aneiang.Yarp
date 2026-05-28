namespace Aneiang.Yarp.Models;

/// <summary>
/// Options for OpenTelemetry distributed tracing integration.
/// Bound from <c>Gateway:Tracing</c> config section.
/// </summary>
public class GatewayTracingOptions
{
    /// <summary>Config section name.</summary>
    public const string SectionName = "Gateway:Tracing";

    /// <summary>
    /// Enable OpenTelemetry tracing. Default: false.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// OTLP exporter endpoint (e.g., "http://localhost:4317"). 
    /// Required when <see cref="Enabled"/> is <c>true</c>.
    /// Can also be set via <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> environment variable.
    /// </summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>
    /// Service name for tracing resource. Default: "Aneiang.Yarp.Gateway".
    /// Can also be set via <c>OTEL_SERVICE_NAME</c> environment variable.
    /// </summary>
    public string ServiceName { get; set; } = "Aneiang.Yarp.Gateway";

    /// <summary>
    /// Enable console exporter for debugging. Default: false.
    /// </summary>
    public bool EnableConsoleExporter { get; set; }

    /// <summary>
    /// Trace sampling rate (0.0 to 1.0). Default: 1.0 (always sample).
    /// </summary>
    public double SamplingRate { get; set; } = 1.0;

    /// <summary>
    /// Propagators to use for trace context propagation.
    /// Supported: "tracecontext", "baggage", "b3", "b3multi".
    /// Default: ["tracecontext", "baggage"] (W3C standard).
    /// Can also be set via <c>OTEL_PROPAGATORS</c> environment variable.
    /// </summary>
    public List<string> Propagators { get; set; } = ["tracecontext", "baggage"];

    /// <summary>
    /// Additional HTTP headers to include as span attributes.
    /// Useful for correlating traces with custom request identifiers.
    /// </summary>
    public List<string>? TraceHeaders { get; set; }
}
