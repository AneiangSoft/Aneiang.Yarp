namespace Aneiang.Yarp.Storage;

/// <summary>Proxy Log entity for database storage.</summary>
public class ProxyLogEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..16];
    public string? Method { get; set; }
    public string? Path { get; set; }
    public string? RouteId { get; set; }
    public string? RouteUid { get; set; }
    public string? RouteKeySnapshot { get; set; }
    public string? ClusterId { get; set; }
    public string? ClusterUid { get; set; }
    public string? ClusterKeySnapshot { get; set; }
    public string? DestinationId { get; set; }
    public string? DestinationUid { get; set; }
    public string? DestinationKeySnapshot { get; set; }
    public int StatusCode { get; set; }
    public long DurationMs { get; set; }
    public long? RequestBodySize { get; set; }
    public long? ResponseBodySize { get; set; }
    public string? ClientIp { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
