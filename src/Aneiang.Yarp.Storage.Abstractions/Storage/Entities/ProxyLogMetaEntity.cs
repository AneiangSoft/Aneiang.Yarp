namespace Aneiang.Yarp.Storage.Entities;

public class ProxyLogMetaEntity
{
    public long Id { get; set; }

    public string Timestamp { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string Level { get; set; } = string.Empty;

    public string? RouteId { get; set; }

    public string? ClusterId { get; set; }

    public string? Method { get; set; }

    public string? UpstreamPath { get; set; }

    public int StatusCode { get; set; }

    public double ElapsedMs { get; set; }

    public string? TraceId { get; set; }

    public int HasRequestBody { get; set; }

    public int HasResponseBody { get; set; }

    public string? DownstreamUrl { get; set; }
}
