namespace Aneiang.Yarp.Storage;

public class DestinationEntity
{
    public string DestinationId { get; set; } = string.Empty;
    public string ClusterId { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? Host { get; set; }
    public bool Healthy { get; set; } = true;
    public string? Metadata { get; set; } // JSON
}
