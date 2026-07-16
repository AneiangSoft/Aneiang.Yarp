namespace Aneiang.Yarp.Storage;

public class PolicyTargetEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string PolicyUid { get; set; } = string.Empty;
    public string PolicyId { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetUid { get; set; } = string.Empty;
    public string TargetKeySnapshot { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
