namespace Aneiang.Yarp.Storage;

public class RouteEntity
{
    public string RouteUid { get; set; } = Guid.NewGuid().ToString("N");
    public string RouteId { get; set; } = string.Empty;
    public string RouteKey
    {
        get => RouteId;
        set => RouteId = value;
    }
    public string? DisplayName { get; set; }
    public string? ClusterUid { get; set; }
    public string ClusterId { get; set; } = string.Empty;
    public string MatchPath { get; set; } = string.Empty;
    public int Order { get; set; } = int.MaxValue;
    public string? Transforms { get; set; } // JSON
    public string Source { get; set; } = "dynamic";
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string? Metadata { get; set; } // JSON

    public string? ConfigJson { get; set; }
}
