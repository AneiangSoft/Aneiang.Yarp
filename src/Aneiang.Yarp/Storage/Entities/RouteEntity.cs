namespace Aneiang.Yarp.Storage;

/// <summary>YARP Route entity for database storage.</summary>
public class RouteEntity
{
    public string RouteUid { get; set; } = Guid.NewGuid().ToString("N");
    public string RouteId { get; set; } = string.Empty;
    public string? ClusterUid { get; set; }
    public string ClusterId { get; set; } = string.Empty;
    public string MatchPath { get; set; } = string.Empty;
    public int Order { get; set; } = 50;
    public string? Transforms { get; set; } // JSON
    public string Source { get; set; } = "dynamic";
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? Metadata { get; set; } // JSON
}
