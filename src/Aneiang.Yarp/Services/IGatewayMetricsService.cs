namespace Aneiang.Yarp.Services;

/// <summary>
/// Interface for collecting and exporting YARP gateway metrics in Prometheus format.
/// </summary>
public interface IGatewayMetricsService
{
    /// <summary>Record a completed proxy request.</summary>
    void RecordRequest(string routeId, string clusterId, string method, int statusCode, double durationMs);

    /// <summary>Increment the active requests gauge.</summary>
    void IncrementActiveRequests(string routeId, string clusterId);

    /// <summary>Decrement the active requests gauge.</summary>
    void DecrementActiveRequests(string routeId, string clusterId);

    /// <summary>Render all collected metrics in Prometheus exposition text format.</summary>
    string GetPrometheusText();
}
