namespace Aneiang.Yarp.Storage;

public class ConfigHistoryEntity
{
    public string VersionId { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ClientIp { get; set; }
    public string ConfigData { get; set; } = "{}"; // Full JSON config
    public string? DiffData { get; set; } // JSON diff
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
