namespace Aneiang.Yarp.Storage;

/// <summary>Policy target binding entity. Transitional bridge from key-based applied_targets JSON to UID-based bindings.</summary>
public class PolicyTargetEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string PolicyUid { get; set; } = string.Empty;
    public string PolicyId { get; set; } = string.Empty;
    /// <summary>"route" | "cluster"</summary>
    public string TargetType { get; set; } = string.Empty;
    public string TargetUid { get; set; } = string.Empty;
    public string TargetKeySnapshot { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
